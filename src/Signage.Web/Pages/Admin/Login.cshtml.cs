using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Signage.Web.Features.Users;

namespace Signage.Web.Pages.Admin;

public class LoginModel(UserAccounts accounts, IStringLocalizer<SharedResource> l) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await accounts.AnyUsersAsync(ct))
        {
            return RedirectToPage("/Admin/Setup");
        }

        return User.Identity?.IsAuthenticated == true ? LocalRedirect(SafeReturnUrl()) : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await accounts.VerifyCredentialsAsync(Input.Email, Input.Password, ct);
        if (user is null)
        {
            // One message for every failure (wrong password, unknown, locked, deactivated): no account enumeration.
            ModelState.AddModelError(string.Empty, l["Login.Error.Failed"]);
            return Page();
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, UserPrincipal.Create(user));
        return LocalRedirect(SafeReturnUrl());
    }

    private string SafeReturnUrl() => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/admin";

    public sealed class LoginInput
    {
        [Required(ErrorMessage = "Validation.Required")]
        [Display(Name = "Users.Field.Email")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Validation.Required")]
        [DataType(DataType.Password)]
        [Display(Name = "Users.Field.Password")]
        public string Password { get; set; } = "";
    }
}
