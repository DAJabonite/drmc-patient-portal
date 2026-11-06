using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account.Manage;

[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class ResetAuthenticatorModel(MfaManagement management) : PageModel
{
    [BindProperty] public MfaPasswordInput Input { get; set; } = new();
    [TempData] public string? StatusMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var result = await management.RunAsync(User, Input.Password, MfaOperation.Reset, token: HttpContext.RequestAborted);
        Input.Password = "";
        ModelState.Remove("Input.Password");
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return Page(); }
        TempData.Remove("RecoveryCodes");
        TempData.Remove("RecoveryCodesOwner");
        if (!await management.RefreshSignInAsync(result.User!, HttpContext.RequestAborted))
            return RedirectToPage("/Account/Login", new { area = "Identity" });
        StatusMessage = "Your authenticator key and recovery codes have been reset. Set up your authenticator app again to enable 2FA.";
        return RedirectToPage("./EnableAuthenticator");
    }
}
