using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Signage.Web.Data;
using Signage.Web.Features.Users;

namespace Signage.Web.Pages.Admin.Account;

public class PasswordModel(UserAccounts accounts, SignageDbContext db, IStringLocalizer<SharedResource> l) : PageModel
{
    [BindProperty]
    public PasswordInput Input { get; set; } = new();

    public bool MustChange => User.MustChangePassword();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userId = User.GetUserId()!.Value;
        var result = await accounts.ChangePasswordAsync(userId, Input.CurrentPassword, Input.NewPassword, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, l[result.ErrorKey!]);
            return Page();
        }

        // The security stamp changed, which ends every other session; renew this one.
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, UserPrincipal.Create(user));
        TempData["Status"] = "Password.Changed";
        return RedirectToPage("/Admin/Index");
    }

    public sealed class PasswordInput
    {
        [Required(ErrorMessage = "Validation.Required")]
        [DataType(DataType.Password)]
        [Display(Name = "Password.Field.Current")]
        public string CurrentPassword { get; set; } = "";

        [Required(ErrorMessage = "Validation.Required")]
        [DataType(DataType.Password)]
        [Display(Name = "Password.Field.New")]
        public string NewPassword { get; set; } = "";

        [Required(ErrorMessage = "Validation.Required")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "Validation.PasswordsDiffer")]
        [Display(Name = "Users.Field.ConfirmPassword")]
        public string ConfirmPassword { get; set; } = "";
    }
}
