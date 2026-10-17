using Signage.Web.Features.Groups;

namespace Signage.Web.Pages.Admin.Groups;

public class IndexModel(GroupService groups) : AdminPageModel
{
    public IReadOnlyList<GroupListItem> Items { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => Items = await groups.ListAsync(ct);
}
