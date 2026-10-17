using Microsoft.EntityFrameworkCore;

namespace Signage.Web.Data;

/// <summary>
/// Keeps every ordered list dense (0, 1, 2, …) after rows disappear through a delete or cascade
/// (ARCHITECTURE §3). Each statement renumbers by the current order, so it's safe to run any time.
/// </summary>
internal static class PositionMaintenance
{
    public static Task RenumberPlaylistsAsync(this SignageDbContext db, IReadOnlyCollection<Guid> playlistIds, CancellationToken ct) =>
        playlistIds.Count == 0 ? Task.CompletedTask : db.Database.ExecuteSqlAsync(
            $"""
            UPDATE playlist_slides AS t SET position = r.rn - 1
            FROM (SELECT id, ROW_NUMBER() OVER (PARTITION BY playlist_id ORDER BY position, id) AS rn
                  FROM playlist_slides WHERE playlist_id = ANY({playlistIds.ToArray()})) AS r
            WHERE t.id = r.id AND t.position <> r.rn - 1
            """, ct);

    public static Task RenumberScreenSlidesAsync(this SignageDbContext db, IReadOnlyCollection<Guid> screenIds, CancellationToken ct) =>
        screenIds.Count == 0 ? Task.CompletedTask : db.Database.ExecuteSqlAsync(
            $"""
            UPDATE screen_slides AS t SET position = r.rn - 1
            FROM (SELECT id, ROW_NUMBER() OVER (PARTITION BY screen_id, placement ORDER BY position, id) AS rn
                  FROM screen_slides WHERE screen_id = ANY({screenIds.ToArray()})) AS r
            WHERE t.id = r.id AND t.position <> r.rn - 1
            """, ct);

    public static Task RenumberScreenPlaylistsAsync(this SignageDbContext db, IReadOnlyCollection<Guid> screenIds, CancellationToken ct) =>
        screenIds.Count == 0 ? Task.CompletedTask : db.Database.ExecuteSqlAsync(
            $"""
            UPDATE screen_playlists AS t SET position = r.rn - 1
            FROM (SELECT screen_id, playlist_id, ROW_NUMBER() OVER (PARTITION BY screen_id ORDER BY position, playlist_id) AS rn
                  FROM screen_playlists WHERE screen_id = ANY({screenIds.ToArray()})) AS r
            WHERE t.screen_id = r.screen_id AND t.playlist_id = r.playlist_id AND t.position <> r.rn - 1
            """, ct);

    public static Task RenumberGroupPlaylistsAsync(this SignageDbContext db, IReadOnlyCollection<Guid> groupIds, CancellationToken ct) =>
        groupIds.Count == 0 ? Task.CompletedTask : db.Database.ExecuteSqlAsync(
            $"""
            UPDATE group_playlists AS t SET position = r.rn - 1
            FROM (SELECT group_id, playlist_id, ROW_NUMBER() OVER (PARTITION BY group_id ORDER BY position, playlist_id) AS rn
                  FROM group_playlists WHERE group_id = ANY({groupIds.ToArray()})) AS r
            WHERE t.group_id = r.group_id AND t.playlist_id = r.playlist_id AND t.position <> r.rn - 1
            """, ct);
}
