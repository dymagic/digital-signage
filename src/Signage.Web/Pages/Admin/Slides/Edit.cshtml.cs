using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Signage.Web.Domain;
using Signage.Web.Features;
using Signage.Web.Features.Settings;
using Signage.Web.Features.Slides;

namespace Signage.Web.Pages.Admin.Slides;

public class EditModel(SlideService slides, SettingsService settings, IStringLocalizer<SharedResource> l) : SlideFormPage(settings, l)
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public uint Version { get; set; }

    /// <summary>The slide as stored (for the preview and heading), not the edited input.</summary>
    public Slide? Saved { get; private set; }

    public string SavedTitle => Saved?.Title ?? "";

    public override SlideType Type => Saved?.Type ?? SlideType.Image;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Saved = await slides.GetAsync(Id, ct);
        if (Saved is null)
        {
            return NotFound();
        }

        var zone = await LoadTimeZoneAsync(ct);
        Version = Saved.Version;
        Input = new SlideInput
        {
            Title = Saved.Title,
            Url = Saved.Url,
            Html = Saved.Html,
            DurationSeconds = Saved.DurationSeconds,
            PlayAudio = Saved.PlayAudio,
            ActiveFrom = Saved.ActiveFromUtc is { } from ? ScheduleClock.ToWallClock(from, zone) : null,
            ActiveUntil = Saved.ActiveUntilUtc is { } until ? ScheduleClock.ToWallClock(until, zone) : null,
            IsEnabled = Saved.IsEnabled,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Saved = await slides.GetAsync(Id, ct);
        if (Saved is null)
        {
            return NotFound();
        }

        var result = await ValidateAsync(ct);
        if (!ModelState.IsValid || result.Slide is null)
        {
            return Page();
        }

        var saved = await slides.UpdateAsync(Id, result.Slide, Version, ct);
        if (!saved.Succeeded)
        {
            if (saved.ErrorKey == CommonErrors.NotFound)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, L[saved.ErrorKey!]);
            return Page();
        }

        KeepWarningForNextPage(result);
        TempData["Status"] = "Common.Saved";
        return RedirectToPage(new { id = Id });
    }
}
