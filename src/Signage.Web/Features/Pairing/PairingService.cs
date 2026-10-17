using Microsoft.EntityFrameworkCore;
using Npgsql;
using Signage.Web.Data;
using Signage.Web.Data.Configurations;
using Signage.Web.Domain;
using Signage.Web.Features.Events;
using Signage.Web.Features.Screens;

namespace Signage.Web.Features.Pairing;

public sealed record PairingStart(Guid PairingId, string Code, string DeviceSecret, DateTimeOffset ExpiresAt);

public enum PairingPollStatus
{
    /// <summary>No such request, or the device secret doesn't match.</summary>
    Unknown,
    Pending,
    Paired,
    /// <summary>Expired, or the token was already delivered once.</summary>
    Gone,
}

public sealed record PairingPoll(PairingPollStatus Status, DateTimeOffset? ExpiresAt = null, string? Token = null, string? ScreenName = null);

/// <summary>
/// R-40 pairing. A device asks for a code; an admin claims the code for a screen; the device's next poll receives
/// the screen token exactly once. The token is minted at that moment, so no usable token is ever stored.
/// </summary>
public sealed class PairingService(SignageDbContext db, TimeProvider time, IScreenNotifier notifier)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Old requests are deleted when new ones are made, once they are this far past expiry.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    public async Task<PairingStart> StartAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var cutoff = now - Retention;
        await db.PairingRequests.Where(p => p.ExpiresAt < cutoff).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        // Unique among requests that can still be claimed or delivered.
        string code;
        var attempts = 0;
        do
        {
            code = PairingCode.Generate();
        }
        while (await db.PairingRequests.AnyAsync(p => p.Code == code && p.ExpiresAt > now && p.TokenDeliveredAt == null, ct).ConfigureAwait(false)
               && ++attempts < 20);

        var secret = ScreenTokens.NewSecret();
        var request = new PairingRequest
        {
            Code = code,
            DeviceSecretHash = ScreenTokens.Hash(secret),
            CreatedAt = now,
            ExpiresAt = now + CodeLifetime,
        };
        db.PairingRequests.Add(request);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PairingStart(request.Id, code, secret, request.ExpiresAt);
    }

    /// <summary>The device polls. Once claimed, the first poll gets the token; later polls get <see cref="PairingPollStatus.Gone"/>.</summary>
    public async Task<PairingPoll> PollAsync(Guid pairingId, string? deviceSecret, CancellationToken ct)
    {
        var request = await db.PairingRequests.AsNoTracking()
            .Where(p => p.Id == pairingId)
            .Select(p => new { p.DeviceSecretHash, p.ExpiresAt, p.ClaimedScreenId, p.TokenDeliveredAt, ScreenName = p.ClaimedScreen != null ? p.ClaimedScreen.Name : null })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (request is null || !ScreenTokens.Matches(deviceSecret, request.DeviceSecretHash))
        {
            return new PairingPoll(PairingPollStatus.Unknown);
        }

        if (request.TokenDeliveredAt is not null)
        {
            return new PairingPoll(PairingPollStatus.Gone);
        }

        var now = time.GetUtcNow();
        if (request.ClaimedScreenId is { } screenId)
        {
            return await DeliverAsync(pairingId, screenId, request.ScreenName!, now, ct).ConfigureAwait(false);
        }

        return request.ExpiresAt <= now ? new PairingPoll(PairingPollStatus.Gone) : new PairingPoll(PairingPollStatus.Pending, request.ExpiresAt);
    }

    /// <summary>
    /// Admin: claims a code for an existing screen. An already paired screen is only accepted for an explicit re-pair
    /// (R-42); the old device keeps working until the new one picks up its token, which replaces the old one.
    /// </summary>
    public async Task<(OperationResult Result, Guid? ScreenId)> ClaimForScreenAsync(string? code, Guid screenId, bool repair, CancellationToken ct)
    {
        var screen = await db.Screens.AsNoTracking().Where(s => s.Id == screenId).Select(s => new { s.TokenHash }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (screen is null)
        {
            return (OperationResult.Fail(CommonErrors.NotFound), null);
        }

        if (screen.TokenHash is not null && !repair)
        {
            return (OperationResult.Fail(PairingErrors.ScreenAlreadyPaired), null);
        }

        var result = await ClaimAsync(code, screenId, ct).ConfigureAwait(false);
        return (result, result.Succeeded ? screenId : null);
    }

    /// <summary>Admin: creates a screen with this name and claims the code for it, in one transaction.</summary>
    public async Task<(OperationResult Result, Guid? ScreenId)> ClaimForNewScreenAsync(string? code, string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (OperationResult.Fail(ScreenErrors.NameRequired), null);
        }

        if (name.Trim().Length > Limits.Name)
        {
            return (OperationResult.Fail(ScreenErrors.NameTooLong), null);
        }

        if (await FindClaimableAsync(code, ct).ConfigureAwait(false) is null)
        {
            return (OperationResult.Fail(PairingErrors.CodeNotFound), null);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var screen = new Screen { Name = name.Trim() };
            db.Screens.Add(screen);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
                return (OperationResult.Fail(ScreenErrors.NameTaken), (Guid?)null);
            }

            var claimed = await ClaimAsync(code, screen.Id, ct).ConfigureAwait(false);
            if (!claimed.Succeeded)
            {
                return (claimed, null);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return (claimed, screen.Id);
        }).ConfigureAwait(false);
    }

    private async Task<OperationResult> ClaimAsync(string? code, Guid screenId, CancellationToken ct)
    {
        var request = await FindClaimableAsync(code, ct).ConfigureAwait(false);
        if (request is null)
        {
            return OperationResult.Fail(PairingErrors.CodeNotFound);
        }

        // Conditional, so two admins typing the same code can't both claim it.
        var now = time.GetUtcNow();
        var claimed = await db.PairingRequests
            .Where(p => p.Id == request.Value && p.ClaimedAt == null && p.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ClaimedScreenId, screenId).SetProperty(p => p.ClaimedAt, now), ct)
            .ConfigureAwait(false);
        return claimed == 1 ? OperationResult.Ok : OperationResult.Fail(PairingErrors.CodeNotFound);
    }

    private async Task<Guid?> FindClaimableAsync(string? code, CancellationToken ct)
    {
        var normalized = PairingCode.Normalize(code);
        if (normalized is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        return await db.PairingRequests
            .Where(p => p.Code == normalized && p.ClaimedAt == null && p.TokenDeliveredAt == null && p.ExpiresAt > now)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    private async Task<PairingPoll> DeliverAsync(Guid pairingId, Guid screenId, string screenName, DateTimeOffset now, CancellationToken ct)
    {
        var token = ScreenTokens.NewSecret();
        var hash = ScreenTokens.Hash(token);
        var strategy = db.Database.CreateExecutionStrategy();
        var delivered = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var marked = await db.PairingRequests.Where(p => p.Id == pairingId && p.TokenDeliveredAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.TokenDeliveredAt, now), ct).ConfigureAwait(false);
            if (marked != 1)
            {
                return false; // a parallel poll got it first
            }

            // Pairing replaces any earlier token of this screen (re-pair). The edit version is deliberately untouched.
            await db.Screens.Where(s => s.Id == screenId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.TokenHash, hash).SetProperty(x => x.PairedAt, now), ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);

        if (!delivered)
        {
            return new PairingPoll(PairingPollStatus.Gone);
        }

        await notifier.ScreensChangedAsync([screenId], ct).ConfigureAwait(false);
        return new PairingPoll(PairingPollStatus.Paired, Token: token, ScreenName: screenName);
    }
}

/// <summary>Localization keys for pairing errors.</summary>
public static class PairingErrors
{
    public const string CodeNotFound = "Pairing.Error.CodeNotFound";
    public const string ScreenAlreadyPaired = "Pairing.Error.ScreenAlreadyPaired";
}
