using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account.Manage;

[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class TwoFactorAuthenticationModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly MfaManagement _management;

    public TwoFactorAuthenticationModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager, MfaManagement management)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _management = management;
    }

    public bool HasAuthenticator { get; set; }
    public int RecoveryCodesLeft { get; set; }
    public bool Is2faEnabled { get; set; }
    [BindProperty] public MfaPasswordInput Input { get; set; } = new();
    public bool IsMachineRemembered { get; set; }
    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        HasAuthenticator = await _userManager.GetAuthenticatorKeyAsync(user) != null;
        Is2faEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
        IsMachineRemembered = await _signInManager.IsTwoFactorClientRememberedAsync(user);
        RecoveryCodesLeft = await _userManager.CountRecoveryCodesAsync(user);

        return Page();
    }

    public async Task<IActionResult> OnPostForgetMachineAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        await _signInManager.ForgetTwoFactorClientAsync();
        StatusMessage = "The current browser has been forgotten. You will need to verify 2FA on your next login.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisable2faAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        var result = await _management.RunAsync(User, Input.Password, MfaOperation.Disable, token: HttpContext.RequestAborted);
        Input.Password = "";
        ModelState.Remove("Input.Password");
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error!);
            return await OnGetAsync();
        }
        TempData.Remove("RecoveryCodes");
        TempData.Remove("RecoveryCodesOwner");
        if (!await _management.RefreshSignInAsync(result.User!, HttpContext.RequestAborted))
            return RedirectToPage("/Account/Login", new { area = "Identity" });
        StatusMessage = "2FA has been disabled. You can re-enable it at any time.";
        return RedirectToPage();
    }
}
