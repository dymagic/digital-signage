using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Signage.Web.Domain;

namespace Signage.Web.Pages.Admin;

/// <summary>
/// Base for admin pages that mix admin-only and editor actions, and that edit lists with htmx (ADR-0006).
/// Folder conventions can't authorize single handlers, so admin-only handlers call <see cref="RequireAdmin"/>.
/// </summary>
public abstract class AdminPageModel : PageModel
{
    public bool IsAdmin => User.IsInRole(nameof(UserRole.Admin));

    public bool IsHtmx => Request.Headers["HX-Request"] == "true";

    /// <summary>Null for admins; otherwise the same 403 response the folder conventions give.</summary>
    protected IActionResult? RequireAdmin() => IsAdmin ? null : Forbid();

    /// <summary>
    /// The item was deleted while the page was open. htmx doesn't swap error responses, so instead of a
    /// silent 404 the browser is sent to the list with a message (HX-Redirect).
    /// </summary>
    protected IActionResult GoneMeanwhile(string listPage)
    {
        TempData["Error"] = "Error.GoneMeanwhile";
        var url = Url.Page(listPage)!;
        if (IsHtmx)
        {
            Response.Headers["HX-Redirect"] = url;
            return new OkResult();
        }

        return Redirect(url);
    }
}
