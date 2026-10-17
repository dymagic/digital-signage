using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Signage.Web.Domain;
using Signage.Web.Features;
using Signage.Web.Features.Groups;
using Signage.Web.Features.Playlists;
using Signage.Web.Features.Screens;

namespace Signage.Web.Pages.Admin.Groups;

/// <summary>One group. Admins edit name, sort order and members; editors and admins edit its playlists.</summary>
public class DetailModel(GroupService groups, ScreenService screens, PlaylistService playlists, IStringLocalizer<SharedResource> l) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public uint Version { get; set; }

    [BindProperty]
    public GroupInput Input { get; set; } = new();

    [BindProperty]
    public List<Guid> ScreenIds { get; set; } = [];

    public ScreenGroup? Group { get; private set; }

    public IReadOnlyList<ScreenListItem> AllScreens { get; private set; } = [];

    public IReadOnlyList<PlaylistListItem> AvailablePlaylists { get; private set; } = [];

    public string? PlaylistsError { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct))
        {
            return NotFound();
        }

        Input = new GroupInput { Name = Group!.Name, Description = Group.Description, SortOrder = Group.SortOrder };
        ScreenIds = Group.Members.Select(m => m.ScreenId).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostDetailsAsync(CancellationToken ct)
    {
        if (RequireAdmin() is { } forbidden)
        {
            return forbidden;
        }

        return await AfterFormPostAsync(await groups.UpdateDetailsAsync(Id, Input.Name, Input.Description, Input.SortOrder, Version, ct), ct);
    }

    public async Task<IActionResult> OnPostMembersAsync(CancellationToken ct)
    {
        if (RequireAdmin() is { } forbidden)
        {
            return forbidden;
        }

        return await AfterFormPostAsync(await groups.SetMembersAsync(Id, ScreenIds, Version, ct), ct);
    }

    public Task<IActionResult> OnPostAddPlaylistAsync(Guid playlistId, CancellationToken ct) =>
        PlaylistsChangeAsync(groups.AddPlaylistAsync(Id, playlistId, Version, ct), ct);

    public Task<IActionResult> OnPostRemovePlaylistAsync(Guid entryId, CancellationToken ct) =>
        PlaylistsChangeAsync(groups.RemovePlaylistAsync(Id, entryId, Version, ct), ct);

    public Task<IActionResult> OnPostMovePlaylistAsync(Guid entryId, int toIndex, CancellationToken ct) =>
        PlaylistsChangeAsync(groups.MovePlaylistAsync(Id, entryId, toIndex, Version, ct), ct);

    private async Task<IActionResult> AfterFormPostAsync(OperationResult result, CancellationToken ct)
    {
        if (result.ErrorKey == CommonErrors.NotFound && !await LoadAsync(ct))
        {
            return GoneMeanwhile("/Admin/Groups/Index");
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, l[result.ErrorKey!]);
            var (submitted, submittedScreens) = (Input, ScreenIds);
            await LoadAsync(ct);
            (Input, ScreenIds) = (submitted, submittedScreens);
            return Page();
        }

        TempData["Status"] = "Common.Saved";
        return RedirectToPage(new { id = Id });
    }

    private async Task<IActionResult> PlaylistsChangeAsync(Task<OperationResult> change, CancellationToken ct)
    {
        var result = await change;
        if (result.ErrorKey == CommonErrors.NotFound && !await LoadAsync(ct))
        {
            return GoneMeanwhile("/Admin/Groups/Index");
        }

        if (!IsHtmx)
        {
            if (!result.Succeeded)
            {
                TempData["Error"] = result.ErrorKey;
            }
            return RedirectToPage(new { id = Id });
        }

        await LoadAsync(ct);
        PlaylistsError = result.Succeeded ? null : result.ErrorKey;
        return Partial("_GroupPlaylists", this);
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Group = await groups.GetAsync(Id, ct);
        if (Group is null)
        {
            return false;
        }

        Version = Group.Version;
        AvailablePlaylists = await playlists.ListAsync(ct);
        AllScreens = IsAdmin ? await screens.ListAsync(ct) : [];
        return true;
    }
}
