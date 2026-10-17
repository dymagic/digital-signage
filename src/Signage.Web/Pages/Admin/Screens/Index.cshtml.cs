using Signage.Web.Features.Screens;

namespace Signage.Web.Pages.Admin.Screens;

public class IndexModel(ScreenService screens) : AdminPageModel
{
    public IReadOnlyList<ScreenListItem> Items { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => Items = await screens.ListAsync(ct);
}
