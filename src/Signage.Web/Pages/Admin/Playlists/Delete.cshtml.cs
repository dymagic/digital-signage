using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Signage.Web.Domain;
using Signage.Web.Features.Playlists;

namespace Signage.Web.Pages.Admin.Playlists;

/// <summary>Deleting a playlist in use asks for confirmation and removes its assignments (ARCHITECTURE §3).</summary>
public class DeleteModel(PlaylistService playlists) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public Playlist? Playlist { get; private set; }

    public PlaylistUsage? Usage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Playlist = await playlists.GetAsync(Id, ct);
        if (Playlist is null)
        {
            return NotFound();
        }

        Usage = await playlists.GetUsageAsync(Id, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!(await playlists.DeleteAsync(Id, ct)).Succeeded)
        {
            return NotFound();
        }

        TempData["Status"] = "Playlists.Deleted";
        return RedirectToPage("Index");
    }
}
