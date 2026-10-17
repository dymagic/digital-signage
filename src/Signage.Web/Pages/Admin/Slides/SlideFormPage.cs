using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Signage.Web.Domain;
using Signage.Web.Features.Settings;
using Signage.Web.Features.Slides;

namespace Signage.Web.Pages.Admin.Slides;

/// <summary>Shared plumbing for the create and edit pages: time zone lookup and validation into ModelState.</summary>
public abstract class SlideFormPage(SettingsService settings, IStringLocalizer<SharedResource> l) : PageModel, ISlideFormPage
{
    public abstract SlideType Type { get; }

    [BindProperty]
    public SlideInput Input { get; set; } = new();

    public string TimeZoneId { get; private set; } = "UTC";

    protected IStringLocalizer<SharedResource> L => l;

    protected async Task<TimeZoneInfo> LoadTimeZoneAsync(CancellationToken ct)
    {
        TimeZoneId = (await settings.GetAsync(ct)).TimeZoneId;
        return ScheduleClock.Resolve(TimeZoneId);
    }

    /// <summary>Validates <see cref="Input"/>; on failure the errors are in ModelState and the result is null.</summary>
    protected async Task<SlideValidation> ValidateAsync(CancellationToken ct)
    {
        var zone = await LoadTimeZoneAsync(ct);
        var result = SlideValidator.Validate(Type, Input, zone);
        foreach (var (field, key) in result.Errors)
        {
            ModelState.AddModelError($"{nameof(Input)}.{field}", l[key]);
        }

        if (result.IsValid && result.Warnings.Count > 0)
        {
            TempData["Warning"] = result.Warnings[0];
        }
        return result;
    }
}
