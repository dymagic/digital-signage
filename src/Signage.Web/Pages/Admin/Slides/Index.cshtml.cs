using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Signage.Web.Domain;
using Signage.Web.Features.Settings;
using Signage.Web.Features.Slides;

namespace Signage.Web.Pages.Admin.Slides;

public class IndexModel(SlideService slides, SettingsService settings, TimeProvider time, IStringLocalizer<SharedResource> l) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public IReadOnlyList<SlideListItem> Items { get; private set; } = [];

    public int DefaultDuration { get; private set; }

    private TimeZoneInfo _zone = TimeZoneInfo.Utc;

    public async Task OnGetAsync(CancellationToken ct)
    {
        var current = await settings.GetAsync(ct);
        DefaultDuration = current.DefaultSlideDurationSeconds;
        _zone = ScheduleClock.Resolve(current.TimeZoneId);
        Items = await slides.ListAsync(Q, ct);
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool enabled, CancellationToken ct)
    {
        var result = await slides.SetEnabledAsync(id, enabled, ct);
        if (!result.Succeeded)
        {
            return NotFound();
        }

        TempData["Status"] = enabled ? "Slides.EnabledStatus" : "Slides.DisabledStatus";
        return RedirectToPage(new { q = Q });
    }

    /// <summary>"Always", "From …", "Until …", "… – …" or "Ended …", in the system time zone.</summary>
    public string DescribeSchedule(Slide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);
        string Format(DateTimeOffset utc) => ScheduleClock.ToWallClock(utc, _zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        return (slide.ActiveFromUtc, slide.ActiveUntilUtc) switch
        {
            (null, null) => l["Slides.Schedule.Always"],
            ({ } from, null) => l["Slides.Schedule.From", Format(from)],
            (null, { } until) when until <= time.GetUtcNow() => l["Slides.Schedule.Ended", Format(until)],
            (null, { } until) => l["Slides.Schedule.Until", Format(until)],
            ({ } from, { } until) when until <= time.GetUtcNow() => l["Slides.Schedule.Ended", Format(until)],
            ({ } from, { } until) => l["Slides.Schedule.Between", Format(from), Format(until)],
        };
    }
}
