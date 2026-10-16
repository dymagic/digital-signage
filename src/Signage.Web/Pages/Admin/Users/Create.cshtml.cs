using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Signage.Web.Domain;
using Signage.Web.Features.Users;

namespace Signage.Web.Pages.Admin.Users;

public class CreateModel(UserAccounts accounts, IStringLocalizer<SharedResource> l) : PageModel
{
    [BindProperty]
    public CreateInput Input { get; set; } = new();

    /// <summary>Shown once on this response and never stored anywhere else.</summary>
    public string? TemporaryPassword { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var (result, temporary) = await accounts.CreateAsync(Input.Email, Input.DisplayName, Input.Role, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, l[result.ErrorKey!]);
            return Page();
        }

        Response.Headers.CacheControl = "no-store";
        TemporaryPassword = temporary;
        return Page();
    }

    public sealed class CreateInput
    {
        [Required(ErrorMessage = "Validation.Required")]
        [Display(Name = "Users.Field.Email")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(100, ErrorMessage = "Validation.MaxLength")]
        [Display(Name = "Users.Field.DisplayName")]
        public string DisplayName { get; set; } = "";

        [Display(Name = "Users.Field.Role")]
        public UserRole Role { get; set; } = UserRole.Editor;
    }
}
