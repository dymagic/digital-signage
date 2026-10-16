namespace Signage.Web.Domain;

/// <summary>Global settings; exactly one row with <see cref="SingletonId"/>.</summary>
public class SystemSettings : IHasUpdatedAt
{
    public const int SingletonId = 1;
    public const int DefaultSlideDuration = 10;
    public const string DefaultTimeZoneId = "UTC";

    public int Id { get; set; } = SingletonId;

    /// <summary>R-02: applies to every non-video slide without its own duration.</summary>
    public int DefaultSlideDurationSeconds { get; set; } = DefaultSlideDuration;

    /// <summary>R-30: show each screen's name and IP at the bottom.</summary>
    public bool ShowInfoOverlay { get; set; }

    /// <summary>IANA time-zone id in which admins enter schedule times (R-03).</summary>
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; set; }
}
