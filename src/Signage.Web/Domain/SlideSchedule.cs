namespace Signage.Web.Domain;

/// <summary>
/// R-03 schedule window. The window is half-open: a slide is active from <c>activeFrom</c>
/// (inclusive) until <c>activeUntil</c> (exclusive). Either bound may be missing. The player
/// mirrors this rule in <c>wwwroot/screen</c> for offline playback (R-24).
/// </summary>
public static class SlideSchedule
{
    public static bool IsActive(DateTimeOffset? activeFromUtc, DateTimeOffset? activeUntilUtc, DateTimeOffset nowUtc) =>
        (activeFromUtc is null || nowUtc >= activeFromUtc) &&
        (activeUntilUtc is null || nowUtc < activeUntilUtc);

    public static bool IsActive(Slide slide, DateTimeOffset nowUtc) =>
        IsActive(slide.ActiveFromUtc, slide.ActiveUntilUtc, nowUtc);
}
