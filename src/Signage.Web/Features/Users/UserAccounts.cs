using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Signage.Web.Data;
using Signage.Web.Domain;

namespace Signage.Web.Features.Users;

/// <summary>
/// Local accounts (R-50–R-52): password checks with lockout, first admin, user administration.
/// Hashing uses ASP.NET Core Identity's <see cref="IPasswordHasher{TUser}"/>. Lockout is implemented
/// here on <see cref="TimeProvider"/> (ADR-0004).
/// </summary>
public sealed partial class UserAccounts(
    SignageDbContext db,
    IPasswordHasher<User> hasher,
    TimeProvider time,
    ILogger<UserAccounts> logger)
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // Serialises "is this the first user / the last admin?" decisions across requests and replicas.
    private const long AccountsLockKey = 0x5167_6E61_6765_0001;

    // Hash of a random password, verified against when the email is unknown, so timing doesn't reveal accounts.
    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<User>().HashPassword(
        new User { Email = "", NormalizedEmail = "", DisplayName = "" }, PasswordPolicy.GenerateTemporary()));

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    public Task<bool> AnyUsersAsync(CancellationToken ct) => db.Users.AnyAsync(ct);

    /// <summary>
    /// Checks credentials. Returns the user only if the password is right, the account is active and not
    /// locked out. Every failure looks the same to the caller; the reason is logged.
    /// </summary>
    public async Task<User?> VerifyCredentialsAsync(string email, string password, CancellationToken ct)
    {
        var normalized = NormalizeEmail(email);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, ct).ConfigureAwait(false);
        if (user is null || user.PasswordHash is null)
        {
            VerifyDummy(password);
            LogSignInFailed(normalized, "unknown user");
            return null;
        }

        var check = await CheckPasswordWithLockoutAsync(user, password, ct).ConfigureAwait(false);
        if (!check.Succeeded)
        {
            LogSignInFailed(user.Email, check.Reason);
            return null;
        }

        if (!user.IsActive)
        {
            LogSignInFailed(user.Email, "deactivated");
            return null;
        }

        LogSignedIn(user.Email);
        return user;
    }

    /// <summary>Creates the first admin, only while there are no users at all (R-51).</summary>
    public async Task<OperationResult> CreateFirstAdminAsync(string email, string displayName, string password, bool mustChangePassword, CancellationToken ct)
    {
        var validation = ValidateNew(email, password);
        if (validation is not null)
        {
            return OperationResult.Fail(validation);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await LockAccountsAsync(ct).ConfigureAwait(false);
            if (await db.Users.AnyAsync(ct).ConfigureAwait(false))
            {
                return OperationResult.Fail(AccountErrors.SetupAlreadyDone);
            }

            db.Users.Add(NewUser(email, displayName, UserRole.Admin, password, mustChangePassword));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            LogFirstAdminCreated(email);
            return OperationResult.Ok;
        }).ConfigureAwait(false);
    }

    /// <summary>Creates a user with a temporary password they must change at first login (R-52).</summary>
    public async Task<(OperationResult Result, string? TemporaryPassword)> CreateAsync(string email, string displayName, UserRole role, CancellationToken ct)
    {
        var temporary = PasswordPolicy.GenerateTemporary();
        var validation = ValidateNew(email, temporary);
        if (validation is not null)
        {
            return (OperationResult.Fail(validation), null);
        }

        var normalized = NormalizeEmail(email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct).ConfigureAwait(false))
        {
            return (OperationResult.Fail(AccountErrors.EmailTaken), null);
        }

        db.Users.Add(NewUser(email, displayName, role, temporary, mustChangePassword: true));
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return (OperationResult.Fail(AccountErrors.EmailTaken), null);
        }

        LogUserCreated(email, role);
        return (OperationResult.Ok, temporary);
    }

    /// <summary>The user changes their own password. Rotates the security stamp (other sessions end).</summary>
    public async Task<OperationResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user?.PasswordHash is null)
        {
            return OperationResult.Fail(CommonErrors.NotFound);
        }

        if (PasswordPolicy.Validate(newPassword) is { } error)
        {
            return OperationResult.Fail(error);
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return OperationResult.Fail(AccountErrors.SamePassword);
        }

        // Wrong current passwords count towards lockout too, so a hijacked session can't brute-force the password.
        var check = await CheckPasswordWithLockoutAsync(user, currentPassword, ct).ConfigureAwait(false);
        if (!check.Succeeded)
        {
            LogPasswordChangeRejected(user.Email, check.Reason);
            return OperationResult.Fail(AccountErrors.WrongCurrentPassword);
        }

        // Write only if the hash is still the one we just verified: if an admin reset the password meanwhile,
        // the reset wins and this change is refused. The admin edit version is deliberately not touched.
        var verifiedHash = user.PasswordHash;
        var newHash = hasher.HashPassword(user, newPassword);
        var newStamp = NewStamp();
        var updated = await db.Users.Where(u => u.Id == userId && u.PasswordHash == verifiedHash).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.PasswordHash, newHash)
            .SetProperty(u => u.MustChangePassword, false)
            .SetProperty(u => u.SecurityStamp, newStamp), ct).ConfigureAwait(false);
        if (updated == 0)
        {
            LogPasswordChangeRejected(user.Email, "password changed or reset meanwhile");
            return OperationResult.Fail(CommonErrors.Concurrency);
        }

        LogPasswordChanged(user.Email);
        return OperationResult.Ok;
    }

    /// <summary>
    /// Verifies a password under the lockout rules. Each attempt first claims a try in one atomic UPDATE that
    /// only succeeds while the account isn't locked, and that sets the lockout when the claim reaches
    /// <see cref="MaxFailedAttempts"/>. PostgreSQL row locking serialises the claims, so at most
    /// <see cref="MaxFailedAttempts"/> passwords are ever checked per lockout window, however many requests run
    /// in parallel. A correct password gives the claimed try back. A locked account still pays for a dummy hash,
    /// so timing doesn't reveal the lockout.
    /// </summary>
    /// <returns>The successful result, or a failure with the reason (for the log only).</returns>
    private async Task<PasswordCheck> CheckPasswordWithLockoutAsync(User user, string password, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var lockoutEnd = now + LockoutDuration;
        var claimed = await db.Database.SqlQuery<int>(
            $"""
            INSERT INTO user_lockouts AS l (user_id, access_failed_count, lockout_end)
            VALUES ({user.Id}, 1, NULL)
            ON CONFLICT (user_id) DO UPDATE SET
                access_failed_count = CASE WHEN l.lockout_end <= {now} THEN 1 ELSE l.access_failed_count + 1 END,
                lockout_end = CASE
                    WHEN (CASE WHEN l.lockout_end <= {now} THEN 1 ELSE l.access_failed_count + 1 END) >= {MaxFailedAttempts} THEN {lockoutEnd}
                    WHEN l.lockout_end <= {now} THEN NULL
                    ELSE l.lockout_end
                END
            WHERE l.lockout_end IS NULL OR l.lockout_end <= {now}
            RETURNING access_failed_count AS "Value"
            """).ToListAsync(ct).ConfigureAwait(false);

        if (claimed is not [var attempt])
        {
            VerifyDummy(password);
            return PasswordCheck.Fail("locked out");
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash!, password);
        if (result == PasswordVerificationResult.Failed)
        {
            if (attempt >= MaxFailedAttempts)
            {
                LogLockedOut(user.Email, MaxFailedAttempts);
            }
            return PasswordCheck.Fail("wrong password");
        }

        // Success clears the failures (the user proved they know the password).
        await db.UserLockouts.Where(l => l.UserId == user.Id).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        // Rehash only if the hash is still the one we verified, so a concurrent password change or reset is never undone.
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var verifiedHash = user.PasswordHash;
            var upgraded = hasher.HashPassword(user, password);
            await db.Users.Where(u => u.Id == user.Id && u.PasswordHash == verifiedHash)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, upgraded), ct).ConfigureAwait(false);
        }
        return PasswordCheck.Ok;
    }

    private void VerifyDummy(string password) =>
        hasher.VerifyHashedPassword(new User { Email = "", NormalizedEmail = "", DisplayName = "" }, DummyHash.Value, password);

    private readonly record struct PasswordCheck(bool Succeeded, string Reason)
    {
        public static PasswordCheck Ok => new(true, "");

        public static PasswordCheck Fail(string reason) => new(false, reason);
    }

    /// <summary>An admin resets a user's password to a temporary one (R-52). Ends the user's sessions.</summary>
    public async Task<(OperationResult Result, string? TemporaryPassword)> ResetPasswordAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return (OperationResult.Fail(CommonErrors.NotFound), null);
        }

        // Unconditional (no version check): a reset must win against anything happening to the account,
        // including an attacker signing in with a leaked password at the same moment.
        var temporary = PasswordPolicy.GenerateTemporary();
        var hash = hasher.HashPassword(user, temporary);
        var stamp = NewStamp();
        var updated = await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.PasswordHash, hash)
            .SetProperty(u => u.MustChangePassword, true)
            .SetProperty(u => u.SecurityStamp, stamp)
            .SetProperty(u => u.Version, u => u.Version + 1), ct).ConfigureAwait(false);
        if (updated == 0)
        {
            return (OperationResult.Fail(CommonErrors.NotFound), null);
        }

        await db.UserLockouts.Where(l => l.UserId == userId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        LogPasswordReset(user.Email);
        return (OperationResult.Ok, temporary);
    }

    /// <summary>
    /// An admin edits a user (R-52). Role or active-state changes rotate the security stamp so the user's
    /// cookie stops working at once. Admins can't change their own role or deactivate themselves, and the
    /// last active admin can't be demoted or deactivated.
    /// </summary>
    public async Task<OperationResult> UpdateAsync(Guid userId, string displayName, UserRole role, bool isActive, int version, Guid actingUserId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await LockAccountsAsync(ct).ConfigureAwait(false);

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
            if (user is null)
            {
                return OperationResult.Fail(CommonErrors.NotFound);
            }

            db.Entry(user).Property(u => u.Version).OriginalValue = version;
            var accessChanged = user.Role != role || user.IsActive != isActive;
            if (accessChanged && user.Id == actingUserId)
            {
                return OperationResult.Fail(AccountErrors.CannotChangeSelf);
            }

            var losesAdmin = user is { Role: UserRole.Admin, IsActive: true } && (role != UserRole.Admin || !isActive);
            if (losesAdmin && !await db.Users.AnyAsync(u => u.Id != user.Id && u.Role == UserRole.Admin && u.IsActive, ct).ConfigureAwait(false))
            {
                return OperationResult.Fail(AccountErrors.LastAdmin);
            }

            user.DisplayName = displayName.Trim();
            user.Role = role;
            user.IsActive = isActive;
            user.Version++;
            if (accessChanged)
            {
                user.SecurityStamp = NewStamp();
            }

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return OperationResult.Fail(CommonErrors.Concurrency);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            LogUserUpdated(user.Email, role, isActive);
            return OperationResult.Ok;
        }).ConfigureAwait(false);
    }

    private static string? ValidateNew(string email, string password) =>
        !IsValidEmail(email) ? AccountErrors.InvalidEmail : PasswordPolicy.Validate(password);

    private static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Trim().Length <= Data.Configurations.Limits.Email
        && MailAddress.TryCreate(email.Trim(), out var parsed)
        && string.Equals(parsed.Address, email.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NewStamp() => Guid.NewGuid().ToString("N");

    private User NewUser(string email, string displayName, UserRole role, string password, bool mustChangePassword)
    {
        var trimmedEmail = email.Trim();
        var user = new User
        {
            Email = trimmedEmail,
            NormalizedEmail = NormalizeEmail(trimmedEmail),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? trimmedEmail : displayName.Trim(),
            Role = role,
            MustChangePassword = mustChangePassword,
            CreatedAt = time.GetUtcNow(),
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        return user;
    }

    private Task<int> LockAccountsAsync(CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AccountsLockKey})", ct);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sign-in succeeded for {Email}")]
    private partial void LogSignedIn(string email);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in failed for {Email}: {Reason}")]
    private partial void LogSignInFailed(string email, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account {Email} locked out after {Attempts} failed attempts")]
    private partial void LogLockedOut(string email, int attempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "First admin {Email} created")]
    private partial void LogFirstAdminCreated(string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {Email} created with role {Role}")]
    private partial void LogUserCreated(string email, UserRole role);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {Email} changed their password")]
    private partial void LogPasswordChanged(string email);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Password change rejected for {Email}: {Reason}")]
    private partial void LogPasswordChangeRejected(string email, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password of {Email} reset by an admin")]
    private partial void LogPasswordReset(string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {Email} updated: role {Role}, active {IsActive}")]
    private partial void LogUserUpdated(string email, UserRole role, bool isActive);
}
