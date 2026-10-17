using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Signage.Web.Features;
using Signage.Web.Features.Groups;

namespace Signage.Web.Pages.Admin.Groups;

/// <summary>Admin only (folder convention in AdminUi).</summary>
public class CreateModel(GroupService groups, IStringLocalizer<SharedResource> l) : AdminPageModel
{
    [BindProperty]
    public GroupInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            ModelState.AddModelError(string.Empty, l[CommonErrors.InvalidRequest]);
            return Page();
        }

        var (result, id) = await groups.CreateAsync(Input.Name, Input.Description, Input.SortOrder, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, l[result.ErrorKey!]);
            return Page();
        }

        TempData["Status"] = "Groups.Created";
        return RedirectToPage("Detail", new { id });
    }
}
