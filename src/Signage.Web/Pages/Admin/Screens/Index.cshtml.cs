using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Signage.Web.Domain;
using Signage.Web.Features.Events;
using Signage.Web.Features.Screens;
using Signage.Web.Features.Settings;
using Signage.Web.Features.Slides;

namespace Signage.Web.Pages.Admin.Screens;

public class IndexModel(ScreenService screens, SettingsService settings, TimeProvider time, IScreenNotifier notifier) : AdminPageModel
{
    public IReadOnlyList<ScreenListItem> Items { get; private set; } = [];

    /// <summary>The system time zone (Settings), for "last seen".</summary>
    public string TimeZoneId { get; private set; } = SystemSettings.DefaultTimeZoneId;

    public DateTimeOffset Now { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await screens.ListAsync(ct);
        TimeZoneId = (await settings.GetAsync(ct)).TimeZoneId;
        Now = time.GetUtcNow();
    }

    /// <summary>Editor+, R-32: every screen shows its name for 30 s.</summary>
    public async Task<IActionResult> OnPostIdentifyAllAsync(CancellationToken ct)
    {
        await notifier.IdentifyAsync(null, ct);
        TempData["Status"] = "Screens.IdentifyingAll";
        return RedirectToPage();
    }

    public bool IsOnline(ScreenListItem item) => ScreenStatus.IsOnline(item.LastSeenAt, Now);

    public string ToLocal(DateTimeOffset utc) =>
        ScheduleClock.ToWallClock(utc, ScheduleClock.Resolve(TimeZoneId)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
