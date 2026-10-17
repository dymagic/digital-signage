using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Signage.Web.Features.Playlists;

namespace Signage.Web.Pages.Admin.Playlists;

public class CreateModel(PlaylistService playlists, IStringLocalizer<SharedResource> l) : PageModel
{
    [BindProperty]
    public PlaylistDetailsInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var (result, id) = await playlists.CreateAsync(Input.Name, Input.Description, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, l[result.ErrorKey!]);
            return Page();
        }

        TempData["Status"] = "Playlists.Created";
        return RedirectToPage("Edit", new { id });
    }
}
