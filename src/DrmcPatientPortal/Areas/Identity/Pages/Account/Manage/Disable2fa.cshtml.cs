using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account.Manage;

[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class Disable2faModel(MfaManagement management) : PageModel
{
    [BindProperty] public MfaPasswordInput Input { get; set; } = new();
    [TempData] public string? StatusMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var result = await management.RunAsync(User, Input.Password, MfaOperation.Disable, token: HttpContext.RequestAborted);
        Input.Password = "";
        ModelState.Remove("Input.Password");
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return Page(); }
        TempData.Remove("RecoveryCodes");
        TempData.Remove("RecoveryCodesOwner");
        if (!await management.RefreshSignInAsync(result.User!, HttpContext.RequestAborted))
            return RedirectToPage("/Account/Login", new { area = "Identity" });
        StatusMessage = "2FA has been disabled. Set up your authenticator app again to re-enable it.";
        return RedirectToPage("./TwoFactorAuthentication");
    }
}
