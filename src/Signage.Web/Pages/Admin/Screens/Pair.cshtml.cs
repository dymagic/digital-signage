using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Signage.Web.Features.Pairing;
using Signage.Web.Features.Screens;

namespace Signage.Web.Pages.Admin.Screens;

/// <summary>R-40: an admin types the code a device shows and picks a new or an existing unpaired screen. Admin only.</summary>
public class PairModel(PairingService pairing, ScreenService screens, IStringLocalizer<SharedResource> l) : AdminPageModel
{
    [BindProperty]
    [Display(Name = "Pairing.Field.Code")]
    public string? Code { get; set; }

    /// <summary>"new" or "existing".</summary>
    [BindProperty]
    public string Target { get; set; } = "new";

    [BindProperty]
    [Display(Name = "Pairing.Field.NewName")]
    public string? NewName { get; set; }

    [BindProperty(SupportsGet = true)]
    [Display(Name = "Pairing.Field.Screen")]
    public Guid? ScreenId { get; set; }

    public IReadOnlyList<ScreenListItem> UnpairedScreens { get; private set; } = [];

    /// <summary>Set when opened from a paired screen's "Re-pair" button: that screen is offered too.</summary>
    public ScreenListItem? RepairScreen { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (ScreenId is not null && UnpairedScreens.Any(s => s.Id == ScreenId))
        {
            Target = "existing";
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var (result, screenId) = Target == "existing" && ScreenId is { } existing
            ? await pairing.ClaimForScreenAsync(Code, existing, repair: RepairScreen?.Id == existing, ct)
            : await pairing.ClaimForNewScreenAsync(Code, NewName, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, l[result.ErrorKey!]);
            await LoadAsync(ct);
            return Page();
        }

        TempData["Status"] = "Pairing.Claimed";
        return RedirectToPage("Detail", new { id = screenId });
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var all = await screens.ListAsync(ct);
        RepairScreen = all.FirstOrDefault(s => s.Id == ScreenId && s.IsPaired);
        UnpairedScreens = all.Where(s => !s.IsPaired || s.Id == RepairScreen?.Id).ToList();
    }
}
