using Signage.Web.Domain;

namespace Signage.UnitTests.Domain;

/// <summary>Builds in-memory screen graphs for resolver tests. Positions are given explicitly.</summary>
internal static class GraphBuilder
{
    public static Slide Slide(string title, bool enabled = true, DateTimeOffset? from = null, DateTimeOffset? until = null) =>
        new() { Title = title, Type = SlideType.Image, Url = $"https://example.org/{title}.png", IsEnabled = enabled, ActiveFromUtc = from, ActiveUntilUtc = until };

    public static Playlist Playlist(string name, params Slide[] slides)
    {
        var playlist = new Playlist { Name = name };
        for (var i = 0; i < slides.Length; i++)
        {
            playlist.Slides.Add(new PlaylistSlide { Playlist = playlist, PlaylistId = playlist.Id, Slide = slides[i], SlideId = slides[i].Id, Position = i });
        }
        return playlist;
    }

    public static Screen Screen() => new() { Name = "Lobby" };

    public static Screen Prepend(this Screen screen, int position, Slide slide) => screen.Attach(SlidePlacement.Prepend, position, slide);

    public static Screen Append(this Screen screen, int position, Slide slide) => screen.Attach(SlidePlacement.Append, position, slide);

    public static Screen WithPlaylist(this Screen screen, int position, Playlist playlist)
    {
        screen.Playlists.Add(new ScreenPlaylist { Screen = screen, ScreenId = screen.Id, Playlist = playlist, PlaylistId = playlist.Id, Position = position });
        return screen;
    }

    public static ScreenGroup Group(string name, int sortOrder, params Playlist[] playlists)
    {
        var group = new ScreenGroup { Name = name, SortOrder = sortOrder };
        for (var i = 0; i < playlists.Length; i++)
        {
            group.Playlists.Add(new GroupPlaylist { Group = group, GroupId = group.Id, Playlist = playlists[i], PlaylistId = playlists[i].Id, Position = i });
        }
        return group;
    }

    public static Screen InGroup(this Screen screen, ScreenGroup group)
    {
        var member = new ScreenGroupMember { Screen = screen, ScreenId = screen.Id, Group = group, GroupId = group.Id };
        screen.Groups.Add(member);
        group.Members.Add(member);
        return screen;
    }

    private static Screen Attach(this Screen screen, SlidePlacement placement, int position, Slide slide)
    {
        screen.Slides.Add(new ScreenSlide { Screen = screen, ScreenId = screen.Id, Slide = slide, SlideId = slide.Id, Placement = placement, Position = position });
        return screen;
    }
}
