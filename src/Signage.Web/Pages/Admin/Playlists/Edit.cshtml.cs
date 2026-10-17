using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Signage.Web.Domain;
using Signage.Web.Features;
using Signage.Web.Features.Playlists;

namespace Signage.Web.Pages.Admin.Playlists;

public class EditModel(PlaylistService playlists, IStringLocalizer<SharedResource> l) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public PlaylistDetailsInput Input { get; set; } = new();

    /// <summary>The playlist version the form was rendered from (optimistic concurrency).</summary>
    [BindProperty]
    public uint Version { get; set; }

    public Playlist? Playlist { get; private set; }

    public PlaylistUsage? Usage { get; private set; }

    public IReadOnlyList<Slide> AvailableSlides { get; private set; } = [];

    /// <summary>Error to show above the entries after a failed htmx change.</summary>
    public string? EntriesError { get; private set; }

    public bool IsHtmx => Request.Headers["HX-Request"] == "true";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct))
        {
            return NotFound();
        }

        Input = new PlaylistDetailsInput { Name = Playlist!.Name, Description = Playlist.Description };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var result = await playlists.UpdateDetailsAsync(Id, Input.Name, Input.Description, Version, ct);
        if (result.ErrorKey == CommonErrors.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, l[result.ErrorKey!]);
            var submitted = Input;
            await LoadAsync(ct);
            Input = submitted;
            return Page();
        }

        TempData["Status"] = "Common.Saved";
        return RedirectToPage(new { id = Id });
    }

    public Task<IActionResult> OnPostAddAsync(Guid slideId, CancellationToken ct) =>
        EntriesChangeAsync(playlists.AddSlideAsync(Id, slideId, Version, ct), ct);

    public Task<IActionResult> OnPostRemoveAsync(Guid entryId, CancellationToken ct) =>
        EntriesChangeAsync(playlists.RemoveEntryAsync(Id, entryId, Version, ct), ct);

    public Task<IActionResult> OnPostMoveAsync(Guid entryId, int toIndex, CancellationToken ct) =>
        EntriesChangeAsync(playlists.MoveEntryAsync(Id, entryId, toIndex, Version, ct), ct);

    /// <summary>htmx gets the refreshed entries block; a plain form post gets a redirect (with any error in TempData).</summary>
    private async Task<IActionResult> EntriesChangeAsync(Task<OperationResult> change, CancellationToken ct)
    {
        var result = await change;
        if (result.ErrorKey == CommonErrors.NotFound && !await LoadAsync(ct))
        {
            return NotFound();
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
        EntriesError = result.Succeeded ? null : result.ErrorKey;
        return Partial("_PlaylistEntries", this);
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Playlist = await playlists.GetAsync(Id, ct);
        if (Playlist is null)
        {
            return false;
        }

        Version = Playlist.Version;
        Usage = await playlists.GetUsageAsync(Id, ct);
        AvailableSlides = await playlists.SlidesForPickerAsync(ct);
        return true;
    }
}
