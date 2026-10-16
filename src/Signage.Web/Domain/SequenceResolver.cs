namespace Signage.Web.Domain;

public enum SequenceSourceKind
{
    Prepend,
    Group,
    ScreenPlaylist,
    Append,
}

/// <summary>Where a resolved slide came from, for the admin's sequence view (R-13).</summary>
public sealed record SequenceSource(
    SequenceSourceKind Kind,
    Guid? GroupId = null,
    string? GroupName = null,
    Guid? PlaylistId = null,
    string? PlaylistName = null);

public sealed record ResolvedSlide(Slide Slide, SequenceSource Source);

/// <summary>
/// Works out the sequence a screen plays (R-12). Pure: it reads only the loaded graph and the time
/// it is given. The screen must have <c>Slides</c>(+<c>Slide</c>), <c>Playlists</c>(+<c>Playlist.Slides.Slide</c>)
/// and <c>Groups</c>(+<c>Group.Playlists.Playlist.Slides.Slide</c>) loaded.
/// </summary>
public static class SequenceResolver
{
    public static IReadOnlyList<ResolvedSlide> Resolve(Screen screen, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var result = new List<ResolvedSlide>();
        var seenPlaylists = new HashSet<Guid>();

        // 1. Prepended slides.
        AddScreenSlides(SlidePlacement.Prepend, SequenceSourceKind.Prepend);

        // 2. Group playlists: groups by sort order, then name; each group's playlists in order.
        var groups = screen.Groups
            .Select(m => m.Group)
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .ThenBy(g => g.Id);
        foreach (var group in groups)
        {
            foreach (var gp in group.Playlists.OrderBy(p => p.Position))
            {
                AddPlaylist(gp.Playlist, new SequenceSource(SequenceSourceKind.Group, group.Id, group.Name, gp.Playlist.Id, gp.Playlist.Name));
            }
        }

        // 3. Playlists assigned directly to the screen.
        foreach (var sp in screen.Playlists.OrderBy(p => p.Position))
        {
            AddPlaylist(sp.Playlist, new SequenceSource(SequenceSourceKind.ScreenPlaylist, PlaylistId: sp.Playlist.Id, PlaylistName: sp.Playlist.Name));
        }

        // 4. Appended slides.
        AddScreenSlides(SlidePlacement.Append, SequenceSourceKind.Append);

        return result;

        void AddScreenSlides(SlidePlacement placement, SequenceSourceKind kind)
        {
            var source = new SequenceSource(kind);
            foreach (var ss in screen.Slides.Where(s => s.Placement == placement).OrderBy(s => s.Position))
            {
                AddIfPlayable(ss.Slide, source);
            }
        }

        void AddPlaylist(Playlist playlist, SequenceSource source)
        {
            // A playlist that would appear more than once counts only at its first occurrence.
            if (!seenPlaylists.Add(playlist.Id))
            {
                return;
            }

            foreach (var ps in playlist.Slides.OrderBy(s => s.Position))
            {
                AddIfPlayable(ps.Slide, source);
            }
        }

        void AddIfPlayable(Slide slide, SequenceSource source)
        {
            if (slide.IsEnabled && SlideSchedule.IsActive(slide, nowUtc))
            {
                result.Add(new ResolvedSlide(slide, source));
            }
        }
    }
}
