using Microsoft.EntityFrameworkCore;
using Npgsql;
using Signage.Web.Data;
using Signage.Web.Data.Configurations;
using Signage.Web.Domain;
using Signage.Web.Features.Events;

namespace Signage.Web.Features.Groups;

public sealed record GroupListItem(Guid Id, string Name, string? Description, int SortOrder, int ScreenCount, int PlaylistCount);

/// <summary>
/// Screen groups (R-11). Name, description, sort order and members are admin-only; the group's ordered
/// playlists are editor+ (PROJECT_BRIEF §3). Every change bumps the group's version and notifies its screens.
/// </summary>
public sealed class GroupService(SignageDbContext db, IScreenNotifier notifier)
{
    public Task<List<GroupListItem>> ListAsync(CancellationToken ct) =>
        db.ScreenGroups.AsNoTracking()
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Name)
            .Select(g => new GroupListItem(g.Id, g.Name, g.Description, g.SortOrder, g.Members.Count, g.Playlists.Count))
            .ToListAsync(ct);

    /// <summary>The group with members (screens by name) and playlists in order.</summary>
    public async Task<ScreenGroup?> GetAsync(Guid id, CancellationToken ct)
    {
        var group = await db.ScreenGroups.AsNoTracking().AsSplitQuery()
            .Include(g => g.Members).ThenInclude(m => m.Screen)
            .Include(g => g.Playlists).ThenInclude(gp => gp.Playlist)
            .SingleOrDefaultAsync(g => g.Id == id, ct).ConfigureAwait(false);
        if (group is not null)
        {
            group.Members.Sort((a, b) => string.Compare(a.Screen.Name, b.Screen.Name, StringComparison.OrdinalIgnoreCase));
            group.Playlists.Sort((a, b) => a.Position.CompareTo(b.Position));
        }
        return group;
    }

    /// <summary>Admin.</summary>
    public async Task<(OperationResult Result, Guid? Id)> CreateAsync(string? name, string? description, int sortOrder, CancellationToken ct)
    {
        if (Validate(name, description) is { } error)
        {
            return (OperationResult.Fail(error), null);
        }

        var group = new ScreenGroup { Name = name!.Trim(), Description = Normalize(description), SortOrder = sortOrder };
        db.ScreenGroups.Add(group);
        var result = await SaveAsync(ct).ConfigureAwait(false);
        return (result, result.Succeeded ? group.Id : null);
    }

    /// <summary>Admin. The sort order changes where this group's playlists sit in its screens' sequences (R-12).</summary>
    public Task<OperationResult> UpdateDetailsAsync(Guid id, string? name, string? description, int sortOrder, uint version, CancellationToken ct) =>
        Validate(name, description) is { } error
            ? Task.FromResult(OperationResult.Fail(error))
            : ChangeAsync(id, version, group =>
            {
                group.Name = name!.Trim();
                group.Description = Normalize(description);
                group.SortOrder = sortOrder;
                return Task.FromResult<string?>(null);
            }, ct);

    /// <summary>Admin. Replaces the members; both the old and the new members are notified.</summary>
    public Task<OperationResult> SetMembersAsync(Guid id, IReadOnlyCollection<Guid> screenIds, uint version, CancellationToken ct) =>
        ChangeAsync(id, version, async _ =>
        {
            var wanted = screenIds.ToHashSet();
            if (await db.Screens.CountAsync(s => wanted.Contains(s.Id), ct).ConfigureAwait(false) != wanted.Count)
            {
                return CommonErrors.NotFound;
            }

            var current = await db.ScreenGroupMembers.Where(m => m.GroupId == id).ToListAsync(ct).ConfigureAwait(false);
            var removed = current.Where(m => !wanted.Contains(m.ScreenId)).ToList();
            var added = wanted.Except(current.Select(m => m.ScreenId)).ToList();
            db.ScreenGroupMembers.RemoveRange(removed);
            foreach (var screenId in added)
            {
                db.ScreenGroupMembers.Add(new ScreenGroupMember { GroupId = id, ScreenId = screenId });
            }

            // Membership is also edited on the screen page: move those screens' versions on too (see ScreenService).
            var touched = removed.Select(m => m.ScreenId).Concat(added).ToList();
            foreach (var screen in await db.Screens.Where(s => touched.Contains(s.Id)).ToListAsync(ct).ConfigureAwait(false))
            {
                screen.Version++;
            }
            return null;
        }, ct, extraAffected: screenIds);

    /// <summary>Editor+. Assigns a playlist to the group, at the end. Once per group.</summary>
    public Task<OperationResult> AddPlaylistAsync(Guid id, Guid playlistId, uint version, CancellationToken ct) =>
        ChangeAsync(id, version, async _ =>
        {
            if (!await db.Playlists.AnyAsync(p => p.Id == playlistId, ct).ConfigureAwait(false))
            {
                return CommonErrors.NotFound;
            }

            var current = await db.GroupPlaylists.Where(gp => gp.GroupId == id).ToListAsync(ct).ConfigureAwait(false);
            if (current.Any(gp => gp.PlaylistId == playlistId))
            {
                return GroupErrors.PlaylistAlreadyAssigned;
            }

            db.GroupPlaylists.Add(new GroupPlaylist { GroupId = id, PlaylistId = playlistId, Position = current.Count });
            return null;
        }, ct);

    public Task<OperationResult> RemovePlaylistAsync(Guid id, Guid playlistId, uint version, CancellationToken ct) =>
        ChangePlaylistsAsync(id, playlistId, version, (list, entry) =>
        {
            list.Remove(entry);
            db.GroupPlaylists.Remove(entry);
            OrderedList.Renumber(list);
        }, ct);

    public Task<OperationResult> MovePlaylistAsync(Guid id, Guid playlistId, int toIndex, uint version, CancellationToken ct) =>
        ChangePlaylistsAsync(id, playlistId, version, (list, entry) => OrderedList.Move(list, entry, toIndex), ct);

    /// <summary>Admin. Deletes the group (screens, playlists and slides stay); former members are notified.</summary>
    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var members = await AffectedScreens.MembersOfGroupAsync(db, id, ct).ConfigureAwait(false);
        if (await db.ScreenGroups.Where(g => g.Id == id).ExecuteDeleteAsync(ct).ConfigureAwait(false) == 0)
        {
            return OperationResult.Fail(CommonErrors.NotFound);
        }

        await notifier.ScreensChangedAsync(members, ct).ConfigureAwait(false);
        return OperationResult.Ok;
    }

    private Task<OperationResult> ChangePlaylistsAsync(Guid id, Guid playlistId, uint version, Action<List<GroupPlaylist>, GroupPlaylist> change, CancellationToken ct) =>
        ChangeAsync(id, version, async _ =>
        {
            var list = await db.GroupPlaylists.Where(gp => gp.GroupId == id)
                .OrderBy(gp => gp.Position).ThenBy(gp => gp.PlaylistId).ToListAsync(ct).ConfigureAwait(false);
            var entry = list.SingleOrDefault(gp => gp.PlaylistId == playlistId);
            if (entry is null)
            {
                return CommonErrors.Concurrency;
            }

            change(list, entry);
            return null;
        }, ct);

    private async Task<OperationResult> ChangeAsync(Guid id, uint version, Func<ScreenGroup, Task<string?>> change, CancellationToken ct, IReadOnlyCollection<Guid>? extraAffected = null)
    {
        var group = await db.ScreenGroups.SingleOrDefaultAsync(g => g.Id == id, ct).ConfigureAwait(false);
        if (group is null)
        {
            return OperationResult.Fail(CommonErrors.NotFound);
        }

        db.Entry(group).Property(g => g.Version).OriginalValue = version;
        var before = await AffectedScreens.MembersOfGroupAsync(db, id, ct).ConfigureAwait(false);
        if (await change(group).ConfigureAwait(false) is { } error)
        {
            return OperationResult.Fail(error);
        }

        db.Entry(group).State = EntityState.Modified; // bumps xmin even when only members or playlists changed
        var result = await SaveAsync(ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            await notifier.ScreensChangedAsync(before.Union(extraAffected ?? []).ToList(), ct).ConfigureAwait(false);
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
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_screen_groups_name" })
        {
            return OperationResult.Fail(GroupErrors.NameTaken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation })
        {
            // A concurrent membership or assignment change, or a screen or playlist deleted meanwhile.
            return OperationResult.Fail(CommonErrors.Concurrency);
        }
    }

    private static string? Validate(string? name, string? description) =>
        string.IsNullOrWhiteSpace(name) ? GroupErrors.NameRequired
        : name.Trim().Length > Limits.Name ? GroupErrors.NameTooLong
        : description?.Trim().Length > Limits.Description ? GroupErrors.DescriptionTooLong
        : null;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Localization keys for group errors.</summary>
public static class GroupErrors
{
    public const string NameRequired = "Groups.Error.NameRequired";
    public const string NameTooLong = "Groups.Error.NameTooLong";
    public const string NameTaken = "Groups.Error.NameTaken";
    public const string DescriptionTooLong = "Groups.Error.DescriptionTooLong";
    public const string PlaylistAlreadyAssigned = "Groups.Error.PlaylistAlreadyAssigned";
}
