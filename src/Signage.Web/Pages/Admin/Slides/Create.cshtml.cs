using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Signage.Web.Domain;
using Signage.Web.Features.Settings;
using Signage.Web.Features.Slides;

namespace Signage.Web.Pages.Admin.Slides;

public class CreateModel(SlideService slides, SettingsService settings, IStringLocalizer<SharedResource> l) : SlideFormPage(settings, l)
{
    /// <summary>The slide type by name (<c>?type=Video</c>). Unknown names are a 404.</summary>
    [BindProperty(SupportsGet = true, Name = "type")]
    public string? TypeName { get; set; }

    public override SlideType Type => TryParseType(TypeName) ?? SlideType.Image;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (TryParseType(TypeName) is null)
        {
            return NotFound();
        }

        await LoadTimeZoneAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (TryParseType(TypeName) is not { } type)
        {
            return NotFound();
        }

        var result = await ValidateAsync(ct);
        if (!ModelState.IsValid || result.Slide is null)
        {
            return Page();
        }

        var id = await slides.CreateAsync(type, result.Slide, ct);
        TempData["Status"] = "Slides.Created";
        return RedirectToPage("Edit", new { id });
    }

    private static SlideType? TryParseType(string? name) =>
        Enum.GetValues<SlideType>().Select(t => (SlideType?)t)
            .FirstOrDefault(t => string.Equals(t.ToString(), name, StringComparison.OrdinalIgnoreCase));
}
