using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Signage.Web.Domain;
using Signage.Web.Features;
using Signage.Web.Features.Groups;
using Signage.Web.Features.Playlists;
using Signage.Web.Features.Screens;
using Signage.Web.Features.Settings;

namespace Signage.Web.Pages.Admin.Screens;

/// <summary>
/// One screen. Admins edit details, mode and groups; editors and admins edit what it plays (prepended slides,
/// playlists, appended slides) and see the resolved sequence (R-13).
/// </summary>
public class DetailModel(
    ScreenService screens,
    Signage.Web.Features.Pairing.ScreenAccessService access,
    GroupService groups,
    PlaylistService playlists,
    SettingsService settings,
    TimeProvider time,
    Signage.Web.Features.Events.IScreenNotifier notifier,
    IStringLocalizer<SharedResource> l) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public int Version { get; set; }

    [BindProperty]
    public DetailsInput Details { get; set; } = new();

    [BindProperty]
    public List<Guid> GroupIds { get; set; } = [];

    public Screen? Screen { get; private set; }

    public IReadOnlyList<GroupListItem> AllGroups { get; private set; } = [];

    public IReadOnlyList<Slide> AvailableSlides { get; private set; } = [];

    public IReadOnlyList<PlaylistListItem> AvailablePlaylists { get; private set; } = [];

    public IReadOnlyList<ResolvedSlide> Sequence { get; private set; } = [];

    public int DefaultDuration { get; private set; }

    /// <summary>The system time zone (Settings), for times shown on this page.</summary>
    public string TimeZoneId { get; private set; } = "UTC";

    public string ToLocal(DateTimeOffset utc) =>
        Signage.Web.Features.Slides.ScheduleClock.ToWallClock(utc, Signage.Web.Features.Slides.ScheduleClock.Resolve(TimeZoneId))
            .ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>R-41: the pre-configured address, shown once right after it was created; never stored.</summary>
    public string? ScreenAddress { get; private set; }

    /// <summary>Error shown above the composition after a failed htmx change.</summary>
    public string? CompositionError { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct))
        {
            return NotFound();
        }

        Details = new DetailsInput { Name = Screen!.Name, Location = Screen.Location, Mode = Screen.Mode, SingleVideoUrl = Screen.SingleVideoUrl };
        GroupIds = Screen.Groups.Select(m => m.GroupId).ToList();
        return Page();
    }

    /// <summary>Admin: name, location, mode, single video URL.</summary>
    public async Task<IActionResult> OnPostDetailsAsync(CancellationToken ct)
    {
        if (RequireAdmin() is { } forbidden)
        {
            return forbidden;
        }

        if (!ModelState.IsValid)
        {
            return await AfterFormPostAsync(OperationResult.Fail(CommonErrors.InvalidRequest), [], ct);
        }

        var (result, warnings) = await screens.UpdateDetailsAsync(Id, new ScreenDetails(Details.Name, Details.Location, Details.Mode, Details.SingleVideoUrl), Version, ct);
        return await AfterFormPostAsync(result, warnings, ct);
    }

    /// <summary>Admin: group memberships.</summary>
    public async Task<IActionResult> OnPostGroupsAsync(CancellationToken ct)
    {
        if (RequireAdmin() is { } forbidden)
        {
            return forbidden;
        }

        if (!ModelState.IsValid)
        {
            return await AfterFormPostAsync(OperationResult.Fail(CommonErrors.InvalidRequest), [], ct);
        }

        return await AfterFormPostAsync(await screens.SetGroupsAsync(Id, GroupIds, Version, ct), [], ct);
    }

    /// <summary>Editor+, R-31: the screen shows a pulsing red border and its name for 30 s.</summary>
    public async Task<IActionResult> OnPostIdentifyAsync(CancellationToken ct)
    {
        if (await screens.GetAsync(Id, ct) is null)
        {
            return GoneMeanwhile("/Admin/Screens/Index");
        }

        await notifier.IdentifyAsync([Id], ct);
        TempData["Status"] = "Screens.Identifying";
        return RedirectToPage(new { id = Id });
    }

    /// <summary>Admin, R-42: the device stops working at once and returns to the pairing screen.</summary>
    public async Task<IActionResult> OnPostRevokeAsync(CancellationToken ct)
    {
        if (RequireAdmin() is { } forbidden)
        {
            return forbidden;
        }

        if (!(await access.RevokeAsync(Id, ct)).Succeeded)
        {
            return GoneMeanwhile("/Admin/Screens/Index");
        }

        TempData["Status"] = "Pairing.Revoked";
        return RedirectToPage(new { id = Id });
    }

    /// <summary>Admin, R-41: a new screen address with an embedded token, replacing any earlier pairing.</summary>
    public async Task<IActionResult> OnPostAddressAsync(CancellationToken ct)
    {
        if (RequireAdmin() is { } forbidden)
        {
            return forbidden;
        }

        var token = await access.CreateAddressTokenAsync(Id, ct);
        if (token is null || !await LoadAsync(ct))
        {
            return GoneMeanwhile("/Admin/Screens/Index");
        }

        Details = new DetailsInput { Name = Screen!.Name, Location = Screen.Location, Mode = Screen.Mode, SingleVideoUrl = Screen.SingleVideoUrl };
        GroupIds = Screen.Groups.Select(m => m.GroupId).ToList();
        ScreenAddress = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/screen?token={Uri.EscapeDataString(token)}";
        Response.Headers.CacheControl = "no-store";
        return Page();
    }

    public Task<IActionResult> OnPostAddSlideAsync(SlidePlacement placement, Guid slideId, CancellationToken ct) =>
        CompositionChangeAsync(() => screens.AddSlideAsync(Id, placement, slideId, Version, ct), ct);

    public Task<IActionResult> OnPostRemoveSlideAsync(Guid entryId, CancellationToken ct) =>
        CompositionChangeAsync(() => screens.RemoveSlideAsync(Id, entryId, Version, ct), ct);

    public Task<IActionResult> OnPostMoveSlideAsync(Guid entryId, int toIndex, CancellationToken ct) =>
        CompositionChangeAsync(() => screens.MoveSlideAsync(Id, entryId, toIndex, Version, ct), ct);

    public Task<IActionResult> OnPostAddPlaylistAsync(Guid playlistId, CancellationToken ct) =>
        CompositionChangeAsync(() => screens.AddPlaylistAsync(Id, playlistId, Version, ct), ct);

    public Task<IActionResult> OnPostRemovePlaylistAsync(Guid entryId, CancellationToken ct) =>
        CompositionChangeAsync(() => screens.RemovePlaylistAsync(Id, entryId, Version, ct), ct);

    public Task<IActionResult> OnPostMovePlaylistAsync(Guid entryId, int toIndex, CancellationToken ct) =>
        CompositionChangeAsync(() => screens.MovePlaylistAsync(Id, entryId, toIndex, Version, ct), ct);

    public string DescribeSource(SequenceSource source) => source switch
    {
        { Kind: SequenceSourceKind.Prepend } => l["Screens.Source.Prepend"],
        { Kind: SequenceSourceKind.Append } => l["Screens.Source.Append"],
        { Kind: SequenceSourceKind.Group } => l["Screens.Source.Group", source.GroupName ?? "", source.PlaylistName ?? ""],
        _ => l["Screens.Source.Playlist", source.PlaylistName ?? ""],
    };

    private async Task<IActionResult> AfterFormPostAsync(OperationResult result, IReadOnlyList<string> warnings, CancellationToken ct)
    {
        if (result.ErrorKey == CommonErrors.NotFound && !await LoadAsync(ct))
        {
            return GoneMeanwhile("/Admin/Screens/Index");
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, l[result.ErrorKey!]);
            var (submittedDetails, submittedGroups) = (Details, GroupIds);
            await LoadAsync(ct);
            (Details, GroupIds) = (submittedDetails, submittedGroups);
            return Page();
        }

        TempData["Status"] = warnings.Count > 0 ? warnings[0] : "Common.Saved";
        return RedirectToPage(new { id = Id });
    }

    /// <summary>htmx gets the refreshed composition block; a plain form post gets a redirect.</summary>
    private async Task<IActionResult> CompositionChangeAsync(Func<Task<OperationResult>> change, CancellationToken ct)
    {
        // A value that failed to bind (tampered or outdated form) must not silently become a default.
        var result = ModelState.IsValid ? await change() : OperationResult.Fail(CommonErrors.InvalidRequest);
        if (result.ErrorKey == CommonErrors.NotFound && !await LoadAsync(ct))
        {
            return GoneMeanwhile("/Admin/Screens/Index");
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
        CompositionError = result.Succeeded ? null : result.ErrorKey;
        return Partial("_ScreenComposition", this);
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Screen = await screens.GetAsync(Id, ct);
        if (Screen is null)
        {
            return false;
        }

        Version = Screen.Version;
        Sequence = await screens.ResolveAsync(Id, time.GetUtcNow(), ct) ?? [];
        var current = await settings.GetAsync(ct);
        DefaultDuration = current.DefaultSlideDurationSeconds;
        TimeZoneId = current.TimeZoneId;
        AvailableSlides = await playlists.SlidesForPickerAsync(ct);
        AvailablePlaylists = await playlists.ListAsync(ct);
        AllGroups = IsAdmin ? await groups.ListAsync(ct) : [];
        return true;
    }

    public sealed class DetailsInput
    {
        [Display(Name = "Screens.Field.Name")]
        public string? Name { get; set; }

        [Display(Name = "Screens.Field.Location")]
        public string? Location { get; set; }

        [Display(Name = "Screens.Field.Mode")]
        public ScreenMode Mode { get; set; }

        [Display(Name = "Screens.Field.SingleVideoUrl")]
        public string? SingleVideoUrl { get; set; }
    }
}
