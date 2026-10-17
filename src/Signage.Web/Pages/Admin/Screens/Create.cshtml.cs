using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Signage.Web.Features.Screens;

namespace Signage.Web.Pages.Admin.Screens;

/// <summary>Admin only (folder convention in AdminUi).</summary>
public class CreateModel(ScreenService screens, IStringLocalizer<SharedResource> l) : AdminPageModel
{
    [BindProperty]
    [Display(Name = "Screens.Field.Name")]
    public string Name { get; set; } = "";

    [BindProperty]
    [Display(Name = "Screens.Field.Location")]
    public string? Location { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var (result, id) = await screens.CreateAsync(Name, Location, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, l[result.ErrorKey!]);
            return Page();
        }

        TempData["Status"] = "Screens.Created";
        return RedirectToPage("Detail", new { id });
    }
}
