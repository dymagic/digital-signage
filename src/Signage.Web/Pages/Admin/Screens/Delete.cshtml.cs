using Microsoft.AspNetCore.Mvc;
using Signage.Web.Domain;
using Signage.Web.Features.Screens;

namespace Signage.Web.Pages.Admin.Screens;

/// <summary>Admin only (folder convention in AdminUi).</summary>
public class DeleteModel(ScreenService screens) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public Screen? Screen { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Screen = await screens.GetAsync(Id, ct);
        return Screen is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!(await screens.DeleteAsync(Id, ct)).Succeeded)
        {
            return NotFound();
        }

        TempData["Status"] = "Screens.Deleted";
        return RedirectToPage("Index");
    }
}
