using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Signage.Web.Features.Users;

namespace Signage.Web.Pages.Admin;

/// <summary>First-run page (R-51). Exists only while there are no users; afterwards it is a 404.</summary>
public class SetupModel(UserAccounts accounts, IStringLocalizer<SharedResource> l) : PageModel
{
    [BindProperty]
    public SetupInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) =>
        await accounts.AnyUsersAsync(ct) ? NotFound() : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (await accounts.AnyUsersAsync(ct))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await accounts.CreateFirstAdminAsync(Input.Email, Input.DisplayName, Input.Password, mustChangePassword: false, ct);
        if (result.ErrorKey == AccountErrors.SetupAlreadyDone)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, l[result.ErrorKey!]);
            return Page();
        }

        var user = await accounts.VerifyCredentialsAsync(Input.Email, Input.Password, ct);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, UserPrincipal.Create(user!));
        return Redirect("/admin");
    }

    public sealed class SetupInput
    {
        [Required(ErrorMessage = "Validation.Required")]
        [Display(Name = "Users.Field.Email")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(100, ErrorMessage = "Validation.MaxLength")]
        [Display(Name = "Users.Field.DisplayName")]
        public string DisplayName { get; set; } = "";

        [Required(ErrorMessage = "Validation.Required")]
        [DataType(DataType.Password)]
        [Display(Name = "Users.Field.Password")]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Validation.Required")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Validation.PasswordsDiffer")]
        [Display(Name = "Users.Field.ConfirmPassword")]
        public string ConfirmPassword { get; set; } = "";
    }
}
