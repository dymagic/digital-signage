using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Signage.Web.Pages.Admin;

public class ForbiddenModel : PageModel
{
    public void OnGet() => Response.StatusCode = StatusCodes.Status403Forbidden;
}
