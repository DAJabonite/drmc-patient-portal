using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account.Manage;

[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class ShowRecoveryCodesModel(UserManager<ApplicationUser> userManager) : PageModel
{
    [TempData]
    public string[]? RecoveryCodes { get; set; }

    [TempData]
    public string? RecoveryCodesOwner { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        if (RecoveryCodes is not { Length: > 0 } ||
            RecoveryCodesOwner != $"user:{await userManager.GetUserIdAsync(user)}" ||
            !await userManager.GetTwoFactorEnabledAsync(user))
        {
            RecoveryCodes = null;
            RecoveryCodesOwner = null;
            return RedirectToPage("./TwoFactorAuthentication");
        }

        return Page();
    }
}
