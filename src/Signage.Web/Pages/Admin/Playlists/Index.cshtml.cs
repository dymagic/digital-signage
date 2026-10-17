using Microsoft.AspNetCore.Mvc.RazorPages;
using Signage.Web.Features.Playlists;

namespace Signage.Web.Pages.Admin.Playlists;

public class IndexModel(PlaylistService playlists) : PageModel
{
    public IReadOnlyList<PlaylistListItem> Items { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => Items = await playlists.ListAsync(ct);
}
