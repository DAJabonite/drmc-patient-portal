using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account.Manage;

[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class GenerateRecoveryCodesModel(
    UserManager<ApplicationUser> userManager,
    MfaManagement management,
    ILogger<GenerateRecoveryCodesModel> logger) : PageModel
{
    public int RecoveryCodesLeft { get; private set; }
    [BindProperty] public MfaPasswordInput Input { get; set; } = new();

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
        if (!await userManager.GetTwoFactorEnabledAsync(user)) return RedirectToPage("./TwoFactorAuthentication");

        RecoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return NotFound();
        if (!await userManager.GetTwoFactorEnabledAsync(user)) return RedirectToPage("./TwoFactorAuthentication");

        var result = await management.RunAsync(User, Input.Password, MfaOperation.GenerateRecoveryCodes, token: HttpContext.RequestAborted);
        Input.Password = "";
        ModelState.Remove("Input.Password");
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error!);
            RecoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user);
            return Page();
        }
        if (!await management.RefreshSignInAsync(result.User!, HttpContext.RequestAborted))
            return RedirectToPage("/Account/Login", new { area = "Identity" });
        RecoveryCodes = result.RecoveryCodes;
        var userId = await userManager.GetUserIdAsync(user);
        RecoveryCodesOwner = $"user:{userId}";
        StatusMessage = "Your new recovery codes are ready. Save them before leaving this page.";
        logger.LogInformation("User with ID '{UserId}' generated new 2FA recovery codes.", userId);
        return RedirectToPage("./ShowRecoveryCodes");
    }
}
