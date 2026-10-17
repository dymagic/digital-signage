namespace Signage.Web.Domain;

/// <summary>A named set of screens that share playlists (R-11).</summary>
public class ScreenGroup
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Order among groups; ties are broken by name (R-12).</summary>
    public int SortOrder { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; set; }

    public List<ScreenGroupMember> Members { get; set; } = [];
    public List<GroupPlaylist> Playlists { get; set; } = [];
}

public class ScreenGroupMember
{
    public Guid ScreenId { get; set; }
    public Guid GroupId { get; set; }

    public Screen Screen { get; set; } = null!;
    public ScreenGroup Group { get; set; } = null!;
}

public class GroupPlaylist : IPositioned
{
    public Guid GroupId { get; set; }
    public Guid PlaylistId { get; set; }

    /// <summary>Dense, zero-based within the group.</summary>
    public int Position { get; set; }

    public ScreenGroup Group { get; set; } = null!;
    public Playlist Playlist { get; set; } = null!;
}
