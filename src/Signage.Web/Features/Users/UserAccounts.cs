using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Signage.Web.Data;
using Signage.Web.Domain;

using Signage.Web.Features;

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
            hasher.VerifyHashedPassword(new User { Email = "", NormalizedEmail = "", DisplayName = "" }, DummyHash.Value, password);
            LogSignInFailed(normalized, "unknown user");
            return null;
        }

        var now = time.GetUtcNow();
        if (user.LockoutEnd is { } end && end > now)
        {
            LogSignInFailed(user.Email, "locked out");
            return null;
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            // One atomic statement: parallel failed attempts must all count and must not trip over each other.
            var lockoutEnd = now + LockoutDuration;
            var lockedNow = await db.Database.SqlQuery<bool>(
                $"""
                UPDATE users SET
                    access_failed_count = CASE WHEN access_failed_count + 1 >= {MaxFailedAttempts} THEN 0 ELSE access_failed_count + 1 END,
                    lockout_end = CASE WHEN access_failed_count + 1 >= {MaxFailedAttempts} THEN {lockoutEnd} ELSE lockout_end END
                WHERE id = {user.Id}
                RETURNING COALESCE(lockout_end = {lockoutEnd}, FALSE) AS "Value"
                """).ToListAsync(ct).ConfigureAwait(false);
            if (lockedNow is [true])
            {
                LogLockedOut(user.Email, MaxFailedAttempts);
            }
            LogSignInFailed(user.Email, "wrong password");
            return null;
        }

        if (!user.IsActive)
        {
            LogSignInFailed(user.Email, "deactivated");
            return null;
        }

        var newHash = verification == PasswordVerificationResult.SuccessRehashNeeded ? hasher.HashPassword(user, password) : user.PasswordHash;
        await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.AccessFailedCount, 0)
            .SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null)
            .SetProperty(u => u.PasswordHash, newHash), ct).ConfigureAwait(false);
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
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user?.PasswordHash is null)
        {
            return OperationResult.Fail(CommonErrors.NotFound);
        }

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            return OperationResult.Fail(AccountErrors.WrongCurrentPassword);
        }

        if (PasswordPolicy.Validate(newPassword) is { } error)
        {
            return OperationResult.Fail(error);
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return OperationResult.Fail(AccountErrors.SamePassword);
        }

        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.MustChangePassword = false;
        user.SecurityStamp = NewStamp();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogPasswordChanged(user.Email);
        return OperationResult.Ok;
    }

    /// <summary>An admin resets a user's password to a temporary one (R-52). Ends the user's sessions.</summary>
    public async Task<(OperationResult Result, string? TemporaryPassword)> ResetPasswordAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return (OperationResult.Fail(CommonErrors.NotFound), null);
        }

        var temporary = PasswordPolicy.GenerateTemporary();
        user.PasswordHash = hasher.HashPassword(user, temporary);
        user.MustChangePassword = true;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.SecurityStamp = NewStamp();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogPasswordReset(user.Email);
        return (OperationResult.Ok, temporary);
    }

    /// <summary>
    /// An admin edits a user (R-52). Role or active-state changes rotate the security stamp so the user's
    /// cookie stops working at once. Admins can't change their own role or deactivate themselves, and the
    /// last active admin can't be demoted or deactivated.
    /// </summary>
    public async Task<OperationResult> UpdateAsync(Guid userId, string displayName, UserRole role, bool isActive, uint version, Guid actingUserId, CancellationToken ct)
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Password of {Email} reset by an admin")]
    private partial void LogPasswordReset(string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {Email} updated: role {Role}, active {IsActive}")]
    private partial void LogUserUpdated(string email, UserRole role, bool isActive);
}
