namespace Signage.Web.Domain;

/// <summary>A named, ordered, reusable list of slides (R-08).</summary>
public class Playlist : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; set; }

    public List<PlaylistSlide> Slides { get; set; } = [];
}

/// <summary>A slide's place in a playlist. The same slide may appear more than once.</summary>
public class PlaylistSlide : IPositioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PlaylistId { get; set; }
    public Guid SlideId { get; set; }

    /// <summary>Dense, zero-based; rewritten on reorder.</summary>
    public int Position { get; set; }

    public Playlist Playlist { get; set; } = null!;
    public Slide Slide { get; set; } = null!;
}
