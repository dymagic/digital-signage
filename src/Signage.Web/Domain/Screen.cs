namespace Signage.Web.Domain;

/// <summary>A physical display running the player (R-10).</summary>
public class Screen
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public string? Location { get; set; }
    public ScreenMode Mode { get; set; } = ScreenMode.Carousel;
    public string? SingleVideoUrl { get; set; }

    /// <summary>SHA-256 of the screen token (R-53). Null when not paired or revoked.</summary>
    public string? TokenHash { get; set; }

    public DateTimeOffset? PairedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public string? LastIp { get; set; }
    public string? PlayerVersion { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; set; }

    public List<ScreenSlide> Slides { get; set; } = [];
    public List<ScreenPlaylist> Playlists { get; set; } = [];
    public List<ScreenGroupMember> Groups { get; set; } = [];
}

/// <summary>A slide attached directly to a screen, before or after everything else.</summary>
public class ScreenSlide
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ScreenId { get; set; }
    public Guid SlideId { get; set; }
    public SlidePlacement Placement { get; set; }

    /// <summary>Dense, zero-based within (screen, placement).</summary>
    public int Position { get; set; }

    public Screen Screen { get; set; } = null!;
    public Slide Slide { get; set; } = null!;
}

/// <summary>A playlist assigned directly to a screen.</summary>
public class ScreenPlaylist
{
    public Guid ScreenId { get; set; }
    public Guid PlaylistId { get; set; }

    /// <summary>Dense, zero-based within the screen.</summary>
    public int Position { get; set; }

    public Screen Screen { get; set; } = null!;
    public Playlist Playlist { get; set; } = null!;
}
