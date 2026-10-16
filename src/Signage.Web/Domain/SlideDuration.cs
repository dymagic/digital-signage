namespace Signage.Web.Domain;

/// <summary>R-02: how long a slide is shown.</summary>
public static class SlideDuration
{
    /// <summary>
    /// The slide's own duration if set and positive, otherwise the global default. Video slides return
    /// <c>null</c>: they play until the video ends.
    /// </summary>
    public static int? Resolve(Slide slide, int defaultDurationSeconds) =>
        slide.Type == SlideType.Video
            ? null
            : slide.DurationSeconds is > 0 and var own ? own : defaultDurationSeconds;
}
