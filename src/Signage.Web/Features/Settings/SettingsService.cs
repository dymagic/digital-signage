using Microsoft.EntityFrameworkCore;
using Signage.Web.Data;
using Signage.Web.Domain;
using Signage.Web.Features;

namespace Signage.Web.Features.Settings;

/// <summary>Global settings (PROJECT_BRIEF §3: admins only): default duration (R-02), info overlay (R-30), time zone (R-03).</summary>
public sealed class SettingsService(SignageDbContext db)
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

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
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
        return OperationResult.Ok;
    }
}

/// <summary>Localization keys for settings errors.</summary>
public static class SettingsErrors
{
    public const string Duration = "Settings.Error.Duration";
    public const string TimeZone = "Settings.Error.TimeZone";
}
