using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Signage.Web.Pages.Admin;

/// <summary>POST-only (with antiforgery) so a link or image elsewhere can't log the admin out.</summary>
[AllowAnonymous]
public class LogoutModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Admin/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Admin/Login");
    }
}
