using Microsoft.EntityFrameworkCore;
using Signage.Web.Data;
using Signage.Web.Features.Events;

namespace Signage.Web.Features.Pairing;

/// <summary>Admin-side token management (R-41, R-42) and the player's token exchange.</summary>
public sealed class ScreenAccessService(SignageDbContext db, TimeProvider time, IScreenNotifier notifier)
{
    /// <summary>
    /// R-41: a new token for a pre-configured screen address. It replaces the screen's current token, so a device
    /// paired before stops working. The token is returned once and only its hash is kept.
    /// </summary>
    public async Task<string?> CreateAddressTokenAsync(Guid screenId, CancellationToken ct)
    {
        var token = ScreenTokens.NewSecret();
        var hash = ScreenTokens.Hash(token);
        var now = time.GetUtcNow();
        var changed = await ReplaceTokenAsync(screenId, hash, now, ct).ConfigureAwait(false);
        if (!changed)
        {
            return null;
        }

        await notifier.ScreensChangedAsync([screenId], ct).ConfigureAwait(false);
        return token;
    }

    /// <summary>R-42: the token stops working at once; the device goes back to the pairing screen.</summary>
    public async Task<OperationResult> RevokeAsync(Guid screenId, CancellationToken ct)
    {
        if (!await ReplaceTokenAsync(screenId, hash: null, time.GetUtcNow(), ct).ConfigureAwait(false))
        {
            return OperationResult.Fail(CommonErrors.NotFound);
        }

        await notifier.ScreensChangedAsync([screenId], ct).ConfigureAwait(false);
        return OperationResult.Ok;
    }

    /// <summary>The screen a token belongs to, or null. Used by the token exchange and the screen auth handler.</summary>
    public async Task<(Guid Id, string Name)?> FindScreenAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128)
        {
            return null;
        }

        var hash = ScreenTokens.Hash(token);
        var screen = await db.Screens.AsNoTracking().Where(s => s.TokenHash == hash)
            .Select(s => new { s.Id, s.Name }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return screen is null ? null : (screen.Id, screen.Name);
    }

    /// <summary>
    /// Sets (or clears) the screen's token and cancels claims that haven't been collected yet, in one transaction, so a
    /// device polling at that moment can't pick up a token that is about to be replaced.
    /// </summary>
    private async Task<bool> ReplaceTokenAsync(Guid screenId, string? hash, DateTimeOffset now, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await PairingService.CancelPendingClaimsAsync(db, screenId, now, ct).ConfigureAwait(false);
            var updated = await db.Screens.Where(s => s.Id == screenId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.TokenHash, hash)
                    .SetProperty(x => x.PairedAt, hash == null ? null : now), ct).ConfigureAwait(false);
            if (updated == 0)
            {
                return false;
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }
}
