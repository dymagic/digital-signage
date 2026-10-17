using Microsoft.EntityFrameworkCore;
using Signage.Web.Data;
using Signage.Web.Domain;
using Signage.Web.Features.Events;
using Signage.Web.Features;

namespace Signage.Web.Features.Settings;

/// <summary>Global settings (PROJECT_BRIEF §3: admins only): default duration (R-02), info overlay (R-30), time zone (R-03).</summary>
public sealed class SettingsService(SignageDbContext db, IScreenNotifier notifier)
{
    public const int MinDuration = 1;
    public const int MaxDuration = 3600;

    public static IReadOnlyList<string> TimeZoneIds { get; } = TimeZoneInfo.GetSystemTimeZones()
        .Select(z => z.Id)
        .Where(id => id.Contains('/', StringComparison.Ordinal))
        .Append(SystemSettings.DefaultTimeZoneId)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToList();

    public Task<SystemSettings> GetAsync(CancellationToken ct) =>
        db.SystemSettings.AsNoTracking().SingleAsync(s => s.Id == SystemSettings.SingletonId, ct);

    public async Task<OperationResult> UpdateAsync(int defaultDurationSeconds, bool showInfoOverlay, string timeZoneId, uint version, CancellationToken ct)
    {
        if (defaultDurationSeconds is < MinDuration or > MaxDuration)
        {
            return OperationResult.Fail(SettingsErrors.Duration);
        }

        // Only the IANA ids the page offers: the browser player's Intl APIs don't know Windows ids or odd aliases.
        if (!TimeZoneIds.Contains(timeZoneId, StringComparer.Ordinal))
        {
            return OperationResult.Fail(SettingsErrors.TimeZone);
        }

        var settings = await db.SystemSettings.SingleAsync(s => s.Id == SystemSettings.SingletonId, ct).ConfigureAwait(false);
        db.Entry(settings).Property(s => s.Version).OriginalValue = version;
        settings.DefaultSlideDurationSeconds = defaultDurationSeconds;
        settings.ShowInfoOverlay = showInfoOverlay;
        settings.TimeZoneId = timeZoneId;
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult.Fail(CommonErrors.Concurrency);
        }

        // Duration, overlay and time zone reach every screen (R-25).
        await notifier.AllScreensChangedAsync(ct).ConfigureAwait(false);
        return OperationResult.Ok;
    }
}

/// <summary>Localization keys for settings errors.</summary>
public static class SettingsErrors
{
    public const string Duration = "Settings.Error.Duration";
    public const string TimeZone = "Settings.Error.TimeZone";
}
