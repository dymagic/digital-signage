using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Signage.Web.Data;
using Signage.Web.Domain;
using Signage.Web.Features.Users;

namespace Signage.Web.Pages.Admin.Users;

public class EditModel(UserAccounts accounts, SignageDbContext db, IStringLocalizer<SharedResource> l) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public EditInput Input { get; set; } = new();

    public string Email { get; private set; } = "";

    public bool IsSelf => User.GetUserId() == Id;

    /// <summary>Shown once on this response after a reset; never stored.</summary>
    public string? TemporaryPassword { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var user = await LoadAsync(ct);
        if (user is null)
        {
            return NotFound();
        }

        Input = new EditInput { DisplayName = user.DisplayName, Role = user.Role, IsActive = user.IsActive, Version = user.Version };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var user = await LoadAsync(ct);
        if (user is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await accounts.UpdateAsync(Id, Input.DisplayName, Input.Role, Input.IsActive, Input.Version, User.GetUserId()!.Value, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, l[result.ErrorKey!]);
            return Page();
        }

        TempData["Status"] = "Common.Saved";
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(CancellationToken ct)
    {
        var (result, temporary) = await accounts.ResetPasswordAsync(Id, ct);
        if (!result.Succeeded)
        {
            return NotFound();
        }

        var user = (await LoadAsync(ct))!;
        ModelState.Clear();
        Input = new EditInput { DisplayName = user.DisplayName, Role = user.Role, IsActive = user.IsActive, Version = user.Version };
        Response.Headers.CacheControl = "no-store";
        TemporaryPassword = temporary;
        return Page();
    }

    private async Task<User?> LoadAsync(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == Id, ct);
        Email = user?.Email ?? "";
        return user;
    }

    public sealed class EditInput
    {
        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(100, ErrorMessage = "Validation.MaxLength")]
        [Display(Name = "Users.Field.DisplayName")]
        public string DisplayName { get; set; } = "";

        [Display(Name = "Users.Field.Role")]
        public UserRole Role { get; set; }

        [Display(Name = "Users.Field.IsActive")]
        public bool IsActive { get; set; }

        public uint Version { get; set; }
    }
}
