namespace Signage.Web.Domain;

/// <summary>One item of content in the shared slide library (R-01).</summary>
public class Slide
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Title { get; set; }
    public SlideType Type { get; set; }

    /// <summary>Absolute http(s) URL for Image, Video and WebPage slides.</summary>
    public string? Url { get; set; }

    /// <summary>Stored HTML chunk for Html slides (rendered in a sandboxed iframe, R-05).</summary>
    public string? Html { get; set; }

    /// <summary>Overrides the global default duration (R-02). Ignored for Video slides.</summary>
    public int? DurationSeconds { get; set; }

    public bool PlayAudio { get; set; }
    public DateTimeOffset? ActiveFromUtc { get; set; }
    public DateTimeOffset? ActiveUntilUtc { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; set; }
}
