using Microsoft.EntityFrameworkCore;
using Npgsql;
using Signage.Web.Data;
using Signage.Web.Data.Configurations;
using Signage.Web.Domain;
using Signage.Web.Features.Events;
using Signage.Web.Features.Slides;

namespace Signage.Web.Features.Screens;

public sealed record ScreenListItem(Guid Id, string Name, string? Location, ScreenMode Mode, IReadOnlyList<string> Groups, bool IsPaired,
    DateTimeOffset? LastSeenAt = null, string? LastIp = null, string? PlayerVersion = null, string? UserAgent = null);

/// <summary>What an admin edits on a screen (R-10, R-14).</summary>
public sealed record ScreenDetails(string? Name, string? Location, ScreenMode Mode, string? SingleVideoUrl);

/// <summary>
/// Screens and what they play (R-10, R-12, R-14). Details, mode and groups are admin-only (PROJECT_BRIEF §3).
/// Prepended/appended slides and directly assigned playlists are editor+. The pages enforce the split, and each
/// method here is annotated with who may call it. Every change bumps the screen's version and notifies the screen.
/// </summary>
public sealed class ScreenService(SignageDbContext db, IScreenNotifier notifier)
{
    public async Task<IReadOnlyList<ScreenListItem>> ListAsync(CancellationToken ct)
    {
        var rows = await db.Screens.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                s.Id, s.Name, s.Location, s.Mode,
                IsPaired = s.TokenHash != null,
                s.LastSeenAt, s.LastIp, s.PlayerVersion, s.UserAgent,
                Groups = s.Groups.OrderBy(m => m.Group.SortOrder).ThenBy(m => m.Group.Name).Select(m => m.Group.Name).ToList(),
            })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => new ScreenListItem(r.Id, r.Name, r.Location, r.Mode, r.Groups, r.IsPaired, r.LastSeenAt, r.LastIp, r.PlayerVersion, r.UserAgent)).ToList();
    }

    /// <summary>The screen with groups, prepended/appended slides and direct playlists, each list in order.</summary>
    public async Task<Screen?> GetAsync(Guid id, CancellationToken ct)
    {
        var screen = await db.Screens.AsNoTracking().AsSplitQuery()
            .Include(s => s.Groups).ThenInclude(m => m.Group)
            .Include(s => s.Slides).ThenInclude(ss => ss.Slide)
            .Include(s => s.Playlists).ThenInclude(sp => sp.Playlist)
            .SingleOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (screen is not null)
        {
            screen.Slides.Sort((a, b) => (a.Placement, a.Position).CompareTo((b.Placement, b.Position)));
            screen.Playlists.Sort((a, b) => a.Position.CompareTo(b.Position));
            screen.Groups.Sort((a, b) => (a.Group.SortOrder, a.Group.Name).CompareTo((b.Group.SortOrder, b.Group.Name)));
        }
        return screen;
    }

    /// <summary>R-13: the sequence this screen plays at <paramref name="nowUtc"/>, with sources.</summary>
    public async Task<IReadOnlyList<ResolvedSlide>?> ResolveAsync(Guid id, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var graph = await db.LoadScreenForResolutionAsync(id, ct).ConfigureAwait(false);
        return graph is null ? null : SequenceResolver.Resolve(graph, nowUtc);
    }

    /// <summary>Admin. Creates an unpaired screen in carousel mode.</summary>
    public async Task<(OperationResult Result, Guid? Id)> CreateAsync(string? name, string? location, CancellationToken ct)
    {
        if (ValidateNameAndLocation(name, location) is { } error)
        {
            return (OperationResult.Fail(error), null);
        }

        var screen = new Screen { Name = name!.Trim(), Location = Normalize(location) };
        db.Screens.Add(screen);
        var result = await SaveAsync(ct).ConfigureAwait(false);
        return (result, result.Succeeded ? screen.Id : null);
    }

    /// <summary>Admin. Name, location, mode and the single video URL. A mode change applies live (R-14).</summary>
    public async Task<(OperationResult Result, IReadOnlyList<string> Warnings)> UpdateDetailsAsync(Guid id, ScreenDetails details, int version, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(details);
        var warnings = new List<string>();
        var error = ValidateNameAndLocation(details.Name, details.Location);
        if (error is null && !Enum.IsDefined(details.Mode))
        {
            error = CommonErrors.InvalidRequest;
        }
        var url = Normalize(details.SingleVideoUrl);
        if (error is null && url is not null && (url.Length > Limits.Url || !SlideValidator.IsAbsoluteHttpUrl(url)))
        {
            error = ScreenErrors.VideoUrlInvalid;
        }
        else if (error is null && details.Mode == ScreenMode.SingleVideo && url is null)
        {
            error = ScreenErrors.VideoUrlRequired;
        }

        if (error is not null)
        {
            return (OperationResult.Fail(error), warnings);
        }

        if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var parsed) && !parsed.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(SlideErrors.VideoNotMp4Warning);
        }

        var result = await ChangeAsync(id, version, screen =>
        {
            screen.Name = details.Name!.Trim();
            screen.Location = Normalize(details.Location);
            screen.Mode = details.Mode;
            screen.SingleVideoUrl = url;
            return Task.FromResult<string?>(null);
        }, ct).ConfigureAwait(false);
        return (result, warnings);
    }

    /// <summary>Admin. Replaces the screen's group memberships (R-11).</summary>
    public Task<OperationResult> SetGroupsAsync(Guid id, IReadOnlyCollection<Guid> groupIds, int version, CancellationToken ct) =>
        ChangeAsync(id, version, async screen =>
        {
            var wanted = groupIds.ToHashSet();
            var known = await db.ScreenGroups.Where(g => wanted.Contains(g.Id)).Select(g => g.Id).ToListAsync(ct).ConfigureAwait(false);
            if (known.Count != wanted.Count)
            {
                return CommonErrors.NotFound;
            }

            var current = await db.ScreenGroupMembers.Where(m => m.ScreenId == id).ToListAsync(ct).ConfigureAwait(false);
            var removed = current.Where(m => !wanted.Contains(m.GroupId)).ToList();
            var added = wanted.Except(current.Select(m => m.GroupId)).ToList();
            db.ScreenGroupMembers.RemoveRange(removed);
            foreach (var groupId in added)
            {
                db.ScreenGroupMembers.Add(new ScreenGroupMember { ScreenId = id, GroupId = groupId });
            }

            // Membership is also edited on the group page: move those groups' versions on too, so a stale
            // members form there reports a conflict instead of undoing this change.
            var touched = removed.Select(m => m.GroupId).Concat(added).ToList();
            foreach (var group in await db.ScreenGroups.Where(g => touched.Contains(g.Id)).ToListAsync(ct).ConfigureAwait(false))
            {
                db.Entry(group).State = EntityState.Modified;
            }
            return null;
        }, ct);

    /// <summary>Editor+. Adds a slide at the end of the prepended or appended list.</summary>
    public Task<OperationResult> AddSlideAsync(Guid id, SlidePlacement placement, Guid slideId, int version, CancellationToken ct) =>
        ChangeAsync(id, version, async _ =>
        {
            if (!Enum.IsDefined(placement))
            {
                return CommonErrors.InvalidRequest;
            }

            if (!await db.Slides.AnyAsync(s => s.Id == slideId, ct).ConfigureAwait(false))
            {
                return CommonErrors.NotFound;
            }

            var count = await db.ScreenSlides.CountAsync(ss => ss.ScreenId == id && ss.Placement == placement, ct).ConfigureAwait(false);
            db.ScreenSlides.Add(new ScreenSlide { ScreenId = id, SlideId = slideId, Placement = placement, Position = count });
            return null;
        }, ct);

    /// <summary>Editor+. Removes one prepended/appended entry; the rest of that list stays dense.</summary>
    public Task<OperationResult> RemoveSlideAsync(Guid id, Guid entryId, int version, CancellationToken ct) =>
        ChangeSlideListAsync(id, entryId, version, (list, entry) =>
        {
            list.Remove(entry);
            db.ScreenSlides.Remove(entry);
            OrderedList.Renumber(list);
        }, ct);

    /// <summary>Editor+. Moves a prepended/appended entry within its own list.</summary>
    public Task<OperationResult> MoveSlideAsync(Guid id, Guid entryId, int toIndex, int version, CancellationToken ct) =>
        ChangeSlideListAsync(id, entryId, version, (list, entry) => OrderedList.Move(list, entry, toIndex), ct);

    /// <summary>Editor+. Assigns a playlist directly, at the end. A playlist can be assigned once per screen.</summary>
    public Task<OperationResult> AddPlaylistAsync(Guid id, Guid playlistId, int version, CancellationToken ct) =>
        ChangeAsync(id, version, async _ =>
        {
            if (!await db.Playlists.AnyAsync(p => p.Id == playlistId, ct).ConfigureAwait(false))
            {
                return CommonErrors.NotFound;
            }

            var current = await db.ScreenPlaylists.Where(sp => sp.ScreenId == id).ToListAsync(ct).ConfigureAwait(false);
            if (current.Any(sp => sp.PlaylistId == playlistId))
            {
                return ScreenErrors.PlaylistAlreadyAssigned;
            }

            db.ScreenPlaylists.Add(new ScreenPlaylist { ScreenId = id, PlaylistId = playlistId, Position = current.Count });
            return null;
        }, ct);

    public Task<OperationResult> RemovePlaylistAsync(Guid id, Guid playlistId, int version, CancellationToken ct) =>
        ChangePlaylistListAsync(id, playlistId, version, (list, entry) =>
        {
            list.Remove(entry);
            db.ScreenPlaylists.Remove(entry);
            OrderedList.Renumber(list);
        }, ct);

    public Task<OperationResult> MovePlaylistAsync(Guid id, Guid playlistId, int toIndex, int version, CancellationToken ct) =>
        ChangePlaylistListAsync(id, playlistId, version, (list, entry) => OrderedList.Move(list, entry, toIndex), ct);

    /// <summary>
    /// Admin. Deletes the screen and everything attached to it (not the slides or playlists). The screen is notified
    /// so a connected player finds out at once: its token no longer works and it goes back to pairing.
    /// </summary>
    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var deleted = await db.Screens.Where(s => s.Id == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        if (deleted == 0)
        {
            return OperationResult.Fail(CommonErrors.NotFound);
        }

        await notifier.ScreensChangedAsync([id], ct).ConfigureAwait(false);
        return OperationResult.Ok;
    }

    private Task<OperationResult> ChangeSlideListAsync(Guid id, Guid entryId, int version, Action<List<ScreenSlide>, ScreenSlide> change, CancellationToken ct) =>
        ChangeAsync(id, version, async _ =>
        {
            var entry = await db.ScreenSlides.SingleOrDefaultAsync(ss => ss.Id == entryId && ss.ScreenId == id, ct).ConfigureAwait(false);
            if (entry is null)
            {
                return CommonErrors.Concurrency;
            }

            var list = await db.ScreenSlides.Where(ss => ss.ScreenId == id && ss.Placement == entry.Placement)
                .OrderBy(ss => ss.Position).ThenBy(ss => ss.Id).ToListAsync(ct).ConfigureAwait(false);
            change(list, entry);
            return null;
        }, ct);

    private Task<OperationResult> ChangePlaylistListAsync(Guid id, Guid playlistId, int version, Action<List<ScreenPlaylist>, ScreenPlaylist> change, CancellationToken ct) =>
        ChangeAsync(id, version, async _ =>
        {
            var list = await db.ScreenPlaylists.Where(sp => sp.ScreenId == id)
                .OrderBy(sp => sp.Position).ThenBy(sp => sp.PlaylistId).ToListAsync(ct).ConfigureAwait(false);
            var entry = list.SingleOrDefault(sp => sp.PlaylistId == playlistId);
            if (entry is null)
            {
                return CommonErrors.Concurrency;
            }

            change(list, entry);
            return null;
        }, ct);

    /// <summary>Loads the screen version-checked, applies the change, bumps the version, saves and notifies.</summary>
    private async Task<OperationResult> ChangeAsync(Guid id, int version, Func<Screen, Task<string?>> change, CancellationToken ct)
    {
        var screen = await db.Screens.SingleOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (screen is null)
        {
            return OperationResult.Fail(CommonErrors.NotFound);
        }

        db.Entry(screen).Property(s => s.Version).OriginalValue = version;
        if (await change(screen).ConfigureAwait(false) is { } error)
        {
            return OperationResult.Fail(error);
        }

        screen.Version = version + 1;
        var result = await SaveAsync(ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            await notifier.ScreensChangedAsync([id], ct).ConfigureAwait(false);
        }
        return result;
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
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_screens_name" })
        {
            return OperationResult.Fail(ScreenErrors.NameTaken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation })
        {
            // A concurrent membership or assignment change, or a group or playlist deleted meanwhile.
            return OperationResult.Fail(CommonErrors.Concurrency);
        }
    }

    private static string? ValidateNameAndLocation(string? name, string? location) =>
        string.IsNullOrWhiteSpace(name) ? ScreenErrors.NameRequired
        : name.Trim().Length > Limits.Name ? ScreenErrors.NameTooLong
        : location?.Trim().Length > Limits.Description ? ScreenErrors.LocationTooLong
        : null;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Localization keys for screen errors.</summary>
public static class ScreenErrors
{
    public const string NameRequired = "Screens.Error.NameRequired";
    public const string NameTooLong = "Screens.Error.NameTooLong";
    public const string NameTaken = "Screens.Error.NameTaken";
    public const string LocationTooLong = "Screens.Error.LocationTooLong";
    public const string VideoUrlRequired = "Screens.Error.VideoUrlRequired";
    public const string VideoUrlInvalid = "Screens.Error.VideoUrlInvalid";
    public const string PlaylistAlreadyAssigned = "Screens.Error.PlaylistAlreadyAssigned";
}
