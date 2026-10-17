using Microsoft.EntityFrameworkCore;
using Signage.Web.Data;

namespace Signage.Web.Features.Events;

/// <summary>Which screens a change can reach, for <see cref="IScreenNotifier"/>.</summary>
public static class AffectedScreens
{
    /// <summary>Screens that show the playlist directly or through one of their groups.</summary>
    public static async Task<List<Guid>> OfPlaylistAsync(SignageDbContext db, Guid playlistId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var direct = db.ScreenPlaylists.Where(sp => sp.PlaylistId == playlistId).Select(sp => sp.ScreenId);
        var viaGroups = db.GroupPlaylists.Where(gp => gp.PlaylistId == playlistId)
            .SelectMany(gp => gp.Group.Members.Select(m => m.ScreenId));
        return await direct.Union(viaGroups).ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Screens that show the slide: prepended/appended, or in any playlist they show.</summary>
    public static async Task<List<Guid>> OfSlideAsync(SignageDbContext db, Guid slideId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var playlistIds = db.PlaylistSlides.Where(ps => ps.SlideId == slideId).Select(ps => ps.PlaylistId);
        var attached = db.ScreenSlides.Where(ss => ss.SlideId == slideId).Select(ss => ss.ScreenId);
        var direct = db.ScreenPlaylists.Where(sp => playlistIds.Contains(sp.PlaylistId)).Select(sp => sp.ScreenId);
        var viaGroups = db.GroupPlaylists.Where(gp => playlistIds.Contains(gp.PlaylistId))
            .SelectMany(gp => gp.Group.Members.Select(m => m.ScreenId));
        return await attached.Union(direct).Union(viaGroups).ToListAsync(ct).ConfigureAwait(false);
    }

    public static Task<List<Guid>> MembersOfGroupAsync(SignageDbContext db, Guid groupId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.ScreenGroupMembers.Where(m => m.GroupId == groupId).Select(m => m.ScreenId).ToListAsync(ct);
    }
}
