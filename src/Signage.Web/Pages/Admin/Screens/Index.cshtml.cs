using System.Globalization;
using Signage.Web.Domain;
using Signage.Web.Features.Screens;
using Signage.Web.Features.Settings;
using Signage.Web.Features.Slides;

namespace Signage.Web.Pages.Admin.Screens;

public class IndexModel(ScreenService screens, SettingsService settings, TimeProvider time) : AdminPageModel
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

    public bool IsOnline(ScreenListItem item) => ScreenStatus.IsOnline(item.LastSeenAt, Now);

    public string ToLocal(DateTimeOffset utc) =>
        ScheduleClock.ToWallClock(utc, ScheduleClock.Resolve(TimeZoneId)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
