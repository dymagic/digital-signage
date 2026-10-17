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
        var updated = await db.Screens.Where(s => s.Id == screenId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TokenHash, hash).SetProperty(x => x.PairedAt, now), ct).ConfigureAwait(false);
        if (updated == 0)
        {
            return null;
        }

        await notifier.ScreensChangedAsync([screenId], ct).ConfigureAwait(false);
        return token;
    }

    /// <summary>R-42: the token stops working at once; the device goes back to the pairing screen.</summary>
    public async Task<OperationResult> RevokeAsync(Guid screenId, CancellationToken ct)
    {
        var updated = await db.Screens.Where(s => s.Id == screenId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TokenHash, (string?)null).SetProperty(x => x.PairedAt, (DateTimeOffset?)null), ct).ConfigureAwait(false);
        if (updated == 0)
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
}
