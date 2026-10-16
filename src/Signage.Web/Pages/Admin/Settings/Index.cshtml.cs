using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Signage.Web.Features.Settings;

namespace Signage.Web.Pages.Admin.Settings;

public class IndexModel(SettingsService settings, IStringLocalizer<SharedResource> l) : PageModel
{
    [BindProperty]
    public SettingsInput Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var current = await settings.GetAsync(ct);
        Input = new SettingsInput
        {
            DefaultSlideDurationSeconds = current.DefaultSlideDurationSeconds,
            ShowInfoOverlay = current.ShowInfoOverlay,
            TimeZoneId = current.TimeZoneId,
            Version = current.Version,
        };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await settings.UpdateAsync(Input.DefaultSlideDurationSeconds, Input.ShowInfoOverlay, Input.TimeZoneId, Input.Version, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, l[result.ErrorKey!]);
            return Page();
        }

        TempData["Status"] = "Common.Saved";
        return RedirectToPage();
    }

    public sealed class SettingsInput
    {
        [Range(SettingsService.MinDuration, SettingsService.MaxDuration, ErrorMessage = "Settings.Error.Duration")]
        [Display(Name = "Settings.Field.DefaultDuration")]
        public int DefaultSlideDurationSeconds { get; set; }

        [Display(Name = "Settings.Field.ShowInfoOverlay")]
        public bool ShowInfoOverlay { get; set; }

        [Required(ErrorMessage = "Validation.Required")]
        [Display(Name = "Settings.Field.TimeZone")]
        public string TimeZoneId { get; set; } = "UTC";

        public uint Version { get; set; }
    }
}
