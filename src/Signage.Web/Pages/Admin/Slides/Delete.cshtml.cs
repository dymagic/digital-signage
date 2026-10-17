using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Signage.Web.Domain;
using Signage.Web.Features.Slides;

namespace Signage.Web.Pages.Admin.Slides;

/// <summary>Deleting a slide that is in use asks for confirmation and removes its links (ARCHITECTURE §3).</summary>
public class DeleteModel(SlideService slides) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public Slide? Slide { get; private set; }

    public SlideUsage? Usage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Slide = await slides.GetAsync(Id, ct);
        if (Slide is null)
        {
            return NotFound();
        }

        Usage = await slides.GetUsageAsync(Id, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var result = await slides.DeleteAsync(Id, ct);
        if (!result.Succeeded)
        {
            return NotFound();
        }

        TempData["Status"] = "Slides.Deleted";
        return RedirectToPage("Index");
    }
}
