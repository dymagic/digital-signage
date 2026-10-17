using Microsoft.AspNetCore.Mvc;
using Signage.Web.Domain;
using Signage.Web.Features.Groups;

namespace Signage.Web.Pages.Admin.Groups;

/// <summary>Admin only (folder convention in AdminUi).</summary>
public class DeleteModel(GroupService groups) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public ScreenGroup? Group { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Group = await groups.GetAsync(Id, ct);
        return Group is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!(await groups.DeleteAsync(Id, ct)).Succeeded)
        {
            return NotFound();
        }

        TempData["Status"] = "Groups.Deleted";
        return RedirectToPage("Index");
    }
}
