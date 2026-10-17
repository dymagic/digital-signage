using Microsoft.EntityFrameworkCore;
using Npgsql;
using Signage.Web.Data;
using Signage.Web.Data.Configurations;
using Signage.Web.Domain;
using Signage.Web.Features.Events;

namespace Signage.Web.Features.Playlists;

public sealed record PlaylistListItem(Guid Id, string Name, string? Description, int SlideCount, int ScreenCount, int GroupCount, DateTimeOffset UpdatedAt);

/// <summary>Where a playlist is assigned (R-08 "used by"): screens directly and screen groups.</summary>
public sealed record PlaylistUsage(IReadOnlyList<string> Screens, IReadOnlyList<string> Groups)
{
    public bool InUse => Screens.Count > 0 || Groups.Count > 0;
}

/// <summary>
/// Playlists and their ordered slides (R-08). Every change to the entries bumps the playlist's version, so two
/// people editing the same playlist get a conflict instead of silently interleaving changes.
/// </summary>
public sealed class PlaylistService(SignageDbContext db, IScreenNotifier notifier)
{
    public Task<List<PlaylistListItem>> ListAsync(CancellationToken ct) =>
        db.Playlists.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new PlaylistListItem(
                p.Id, p.Name, p.Description, p.Slides.Count,
                db.ScreenPlaylists.Count(sp => sp.PlaylistId == p.Id),
                db.GroupPlaylists.Count(gp => gp.PlaylistId == p.Id),
                p.UpdatedAt))
            .ToListAsync(ct);

    /// <summary>The playlist with its entries in order (each with its slide).</summary>
    public async Task<Playlist?> GetAsync(Guid id, CancellationToken ct)
    {
        var playlist = await db.Playlists.AsNoTracking()
            .Include(p => p.Slides).ThenInclude(ps => ps.Slide)
            .SingleOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);
        playlist?.Slides.Sort((a, b) => a.Position.CompareTo(b.Position));
        return playlist;
    }

    public async Task<PlaylistUsage> GetUsageAsync(Guid id, CancellationToken ct)
    {
        var screens = await db.ScreenPlaylists.Where(sp => sp.PlaylistId == id).Select(sp => sp.Screen.Name).OrderBy(n => n).ToListAsync(ct).ConfigureAwait(false);
        var groups = await db.GroupPlaylists.Where(gp => gp.PlaylistId == id).Select(gp => gp.Group.Name).OrderBy(n => n).ToListAsync(ct).ConfigureAwait(false);
        return new PlaylistUsage(screens, groups);
    }

    /// <summary>Slides that can be added, for the picker.</summary>
    public Task<List<Slide>> SlidesForPickerAsync(CancellationToken ct) =>
        db.Slides.AsNoTracking().OrderBy(s => s.Title).ToListAsync(ct);

    public async Task<(OperationResult Result, Guid? Id)> CreateAsync(string name, string? description, CancellationToken ct)
    {
        if (ValidateDetails(name, description) is { } error)
        {
            return (OperationResult.Fail(error), null);
        }

        var playlist = new Playlist { Name = name.Trim(), Description = Normalize(description) };
        db.Playlists.Add(playlist);
        var result = await SaveAsync(ct).ConfigureAwait(false);
        return (result, result.Succeeded ? playlist.Id : null);
    }

    public async Task<OperationResult> UpdateDetailsAsync(Guid id, string name, string? description, uint version, CancellationToken ct)
    {
        if (ValidateDetails(name, description) is { } error)
        {
            return OperationResult.Fail(error);
        }

        var playlist = await LoadForChangeAsync(id, version, ct).ConfigureAwait(false);
        if (playlist is null)
        {
            return OperationResult.Fail(CommonErrors.NotFound);
        }

        playlist.Name = name.Trim();
        playlist.Description = Normalize(description);
        return await SaveAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Appends a slide. The same slide may appear more than once (R-08).</summary>
    public Task<OperationResult> AddSlideAsync(Guid playlistId, Guid slideId, uint version, CancellationToken ct) =>
        ChangeEntriesAsync(playlistId, version, async entries =>
        {
            if (!await db.Slides.AnyAsync(s => s.Id == slideId, ct).ConfigureAwait(false))
            {
                return CommonErrors.NotFound;
            }

            var entry = new PlaylistSlide { PlaylistId = playlistId, SlideId = slideId, Position = entries.Count };
            entries.Add(entry);
            db.PlaylistSlides.Add(entry);
            return null;
        }, ct);

    public Task<OperationResult> RemoveEntryAsync(Guid playlistId, Guid entryId, uint version, CancellationToken ct) =>
        ChangeEntriesAsync(playlistId, version, entries =>
        {
            var entry = entries.SingleOrDefault(e => e.Id == entryId);
            if (entry is null)
            {
                return Task.FromResult<string?>(CommonErrors.Concurrency);
            }

            entries.Remove(entry);
            db.PlaylistSlides.Remove(entry);
            return Task.FromResult<string?>(null);
        }, ct);

    /// <summary>Moves an entry to <paramref name="toIndex"/> (clamped to the list).</summary>
    public Task<OperationResult> MoveEntryAsync(Guid playlistId, Guid entryId, int toIndex, uint version, CancellationToken ct) =>
        ChangeEntriesAsync(playlistId, version, entries =>
        {
            var entry = entries.SingleOrDefault(e => e.Id == entryId);
            if (entry is null)
            {
                return Task.FromResult<string?>(CommonErrors.Concurrency);
            }

            entries.Remove(entry);
            entries.Insert(Math.Clamp(toIndex, 0, entries.Count), entry);
            return Task.FromResult<string?>(null);
        }, ct);

    /// <summary>Deletes the playlist and its assignments; the affected screens' and groups' lists stay dense.</summary>
    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var screenIds = await db.ScreenPlaylists.Where(sp => sp.PlaylistId == id).Select(sp => sp.ScreenId).ToListAsync(ct).ConfigureAwait(false);
            var groupIds = await db.GroupPlaylists.Where(gp => gp.PlaylistId == id).Select(gp => gp.GroupId).ToListAsync(ct).ConfigureAwait(false);
            var affected = await AffectedScreens.OfPlaylistAsync(db, id, ct).ConfigureAwait(false);
            if (await db.Playlists.Where(p => p.Id == id).ExecuteDeleteAsync(ct).ConfigureAwait(false) == 0)
            {
                return OperationResult.Fail(CommonErrors.NotFound);
            }

            await db.RenumberScreenPlaylistsAsync(screenIds, ct).ConfigureAwait(false);
            await db.RenumberGroupPlaylistsAsync(groupIds, ct).ConfigureAwait(false);

            // Their lists changed under any open editor: move the versions on (as a slide delete does).
            await db.Screens.Where(s => screenIds.Contains(s.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, x => x.Version + 1), ct).ConfigureAwait(false);
            await db.ScreenGroups.Where(g => groupIds.Contains(g.Id)).ExecuteUpdateAsync(s => s.SetProperty(g => g.SortOrder, g => g.SortOrder), ct).ConfigureAwait(false); // rewrites the row: new xmin
            await tx.CommitAsync(ct).ConfigureAwait(false);
            await notifier.ScreensChangedAsync(affected, ct).ConfigureAwait(false);
            return OperationResult.Ok;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the playlist (version-checked) and its entries in order, applies <paramref name="change"/> to the list,
    /// rewrites positions densely and saves. The playlist row is touched too, so its version moves on.
    /// </summary>
    private async Task<OperationResult> ChangeEntriesAsync(Guid playlistId, uint version, Func<List<PlaylistSlide>, Task<string?>> change, CancellationToken ct)
    {
        var playlist = await LoadForChangeAsync(playlistId, version, ct).ConfigureAwait(false);
        if (playlist is null)
        {
            return OperationResult.Fail(CommonErrors.NotFound);
        }

        var entries = await db.PlaylistSlides.Where(ps => ps.PlaylistId == playlistId)
            .OrderBy(ps => ps.Position).ThenBy(ps => ps.Id).ToListAsync(ct).ConfigureAwait(false);
        if (await change(entries).ConfigureAwait(false) is { } error)
        {
            return OperationResult.Fail(error);
        }

        for (var i = 0; i < entries.Count; i++)
        {
            entries[i].Position = i;
        }

        db.Entry(playlist).State = EntityState.Modified; // bumps xmin (and UpdatedAt) even if only entries changed
        var result = await SaveAsync(ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            await notifier.ScreensChangedAsync(await AffectedScreens.OfPlaylistAsync(db, playlistId, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        }
        return result;
    }

    private async Task<Playlist?> LoadForChangeAsync(Guid id, uint version, CancellationToken ct)
    {
        var playlist = await db.Playlists.SingleOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);
        if (playlist is not null)
        {
            db.Entry(playlist).Property(p => p.Version).OriginalValue = version;
        }
        return playlist;
    }

    private async Task<OperationResult> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return OperationResult.Ok;
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult.Fail(CommonErrors.Concurrency);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return OperationResult.Fail(PlaylistErrors.NameTaken);
        }
    }

    private static string? ValidateDetails(string name, string? description) =>
        string.IsNullOrWhiteSpace(name) ? PlaylistErrors.NameRequired
        : name.Trim().Length > Limits.Name ? PlaylistErrors.NameTooLong
        : description?.Trim().Length > Limits.Description ? PlaylistErrors.DescriptionTooLong
        : null;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Localization keys for playlist errors.</summary>
public static class PlaylistErrors
{
    public const string NameRequired = "Playlists.Error.NameRequired";
    public const string NameTooLong = "Playlists.Error.NameTooLong";
    public const string DescriptionTooLong = "Playlists.Error.DescriptionTooLong";
    public const string NameTaken = "Playlists.Error.NameTaken";
}
