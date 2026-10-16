using Microsoft.EntityFrameworkCore;
using Signage.Web.Domain;

namespace Signage.Web.Data;

public static class ScreenGraphQueries
{
    /// <summary>Loads a screen with everything <see cref="SequenceResolver"/> needs, read-only.</summary>
    public static Task<Screen?> LoadScreenForResolutionAsync(this SignageDbContext db, Guid screenId, CancellationToken cancellationToken) =>
        db.Screens
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(s => s.Slides).ThenInclude(ss => ss.Slide)
            .Include(s => s.Playlists).ThenInclude(sp => sp.Playlist).ThenInclude(p => p.Slides).ThenInclude(ps => ps.Slide)
            .Include(s => s.Groups).ThenInclude(m => m.Group).ThenInclude(g => g.Playlists).ThenInclude(gp => gp.Playlist).ThenInclude(p => p.Slides).ThenInclude(ps => ps.Slide)
            .SingleOrDefaultAsync(s => s.Id == screenId, cancellationToken);
}
