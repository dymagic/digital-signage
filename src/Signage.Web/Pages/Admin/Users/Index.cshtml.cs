using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Signage.Web.Data;
using Signage.Web.Domain;

namespace Signage.Web.Pages.Admin.Users;

public class IndexModel(SignageDbContext db) : PageModel
{
    public IReadOnlyList<User> Users { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) =>
        Users = await db.Users.AsNoTracking().OrderBy(u => u.DisplayName).ThenBy(u => u.Email).ToListAsync(ct);
}
