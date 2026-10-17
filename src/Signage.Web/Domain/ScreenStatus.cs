namespace Signage.Web.Domain;

/// <summary>R-33: a screen is online while its last heartbeat (every 30 s) is less than 90 s old.</summary>
public static class ScreenStatus
{
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(90);

    public static bool IsOnline(DateTimeOffset? lastSeenAt, DateTimeOffset now) =>
        lastSeenAt is { } seen && now - seen < OnlineWindow;
}
