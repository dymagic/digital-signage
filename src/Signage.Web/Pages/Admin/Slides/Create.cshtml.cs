using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Signage.Web.Domain;
using Signage.Web.Features.Settings;
using Signage.Web.Features.Slides;

namespace Signage.Web.Pages.Admin.Slides;

public class CreateModel(SlideService slides, SettingsService settings, IStringLocalizer<SharedResource> l) : SlideFormPage(settings, l)
{
    [BindProperty(SupportsGet = true, Name = "type")]
    public SlideType SlideType { get; set; } = SlideType.Image;

    public override SlideType Type => SlideType;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!Enum.IsDefined(SlideType))
        {
            return NotFound();
        }

        await LoadTimeZoneAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!Enum.IsDefined(SlideType))
        {
            return NotFound();
        }

        var result = await ValidateAsync(ct);
        if (!ModelState.IsValid || result.Slide is null)
        {
            return Page();
        }

        var id = await slides.CreateAsync(SlideType, result.Slide, ct);
        TempData["Status"] = "Slides.Created";
        return RedirectToPage("Edit", new { id });
    }
}
