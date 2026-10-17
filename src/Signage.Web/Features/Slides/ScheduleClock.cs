namespace Signage.Web.Features.Slides;

/// <summary>
/// R-03: schedule times are entered and shown in the system time zone (Settings) and stored as UTC.
/// Wall-clock times that don't exist (the hour skipped when clocks go forward) are rejected. Times that
/// occur twice (when clocks go back) mean the second, standard-time occurrence (ADR-0005).
/// </summary>
public static class ScheduleClock
{
    public static TimeZoneInfo Resolve(string timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>Converts a wall-clock time in <paramref name="zone"/> to UTC; false if that time doesn't exist there.</summary>
    public static bool TryToUtc(DateTime wallClock, TimeZoneInfo zone, out DateTimeOffset utc)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            utc = default;
            return false;
        }

        utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone), TimeSpan.Zero);
        return true;
    }

    /// <summary>The wall-clock time in <paramref name="zone"/> for a stored UTC instant (for forms and lists).</summary>
    public static DateTime ToWallClock(DateTimeOffset utc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(utc, zone).DateTime, DateTimeKind.Unspecified);
    }
}
