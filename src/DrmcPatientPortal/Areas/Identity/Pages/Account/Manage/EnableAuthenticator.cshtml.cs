using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account.Manage;

[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class EnableAuthenticatorModel(UserManager<ApplicationUser> users, MfaManagement management,
    IQrCodeService qrCodes, UrlEncoder encoder,
    ILogger<EnableAuthenticatorModel> logger) : PageModel
{
    public string SharedKey { get; private set; } = "";
    public string QrCodeSvg { get; private set; } = "";
    public bool ShowSetup => SharedKey.Length > 0;

    [TempData] public string[]? RecoveryCodes { get; set; }
    [TempData] public string? RecoveryCodesOwner { get; set; }
    [TempData] public string? StatusMessage { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required, DataType(DataType.Password), Display(Name = "Current password")]
        public string Password { get; set; } = "";
        [Required, StringLength(7, MinimumLength = 6), Display(Name = "6-Digit Verification Code")]
        public string Code { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return NotFound();
        return user.TwoFactorEnabled ? RedirectToPage("./TwoFactorAuthentication") : Page();
    }

    public async Task<IActionResult> OnPostShowSetupAsync()
    {
        ModelState.Remove("Input.Code");
        var result = await management.RunAsync(User, Input.Password, MfaOperation.ShowSetup, token: HttpContext.RequestAborted);
        ClearPassword();
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return Page(); }
        RecoveryCodes = null;
        RecoveryCodesOwner = null;
        if (!await management.RefreshSignInAsync(result.User!, HttpContext.RequestAborted))
            return RedirectToPage("/Account/Login", new { area = "Identity" });
        await LoadQrAsync(result.User!, result.SetupKey!);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var result = await management.RunAsync(User, Input.Password, MfaOperation.Enable, Input.Code, HttpContext.RequestAborted);
        ClearPassword();
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error!);
            if (result.SetupKey is not null)
            {
                var user = await users.GetUserAsync(User);
                if (user is not null) await LoadQrAsync(user, result.SetupKey);
            }
            return Page();
        }

        // Invitation activation owns a separate transaction and can update the stamp again.
        var role = await management.ActivateInvitationAsync(result.User!, HttpContext.RequestAborted);
        if (!await management.RefreshSignInAsync(result.User!, HttpContext.RequestAborted))
            return RedirectToPage("/Account/Login", new { area = "Identity" });
        StatusMessage = "Your authenticator app has been verified and 2FA is now active.";
        if (role is not null)
            StatusMessage += $" Your {DrmcPatientPortal.Areas.Admin.Security.StaffRoles.Label(role).ToLowerInvariant()} role is now active. Sign out, then sign in with your authenticator code to open Administration.";
        RecoveryCodes = result.RecoveryCodes;
        RecoveryCodesOwner = $"user:{result.User!.Id}";
        logger.LogInformation("User with ID '{UserId}' enabled 2FA with an authenticator app.", result.User.Id);
        return RedirectToPage("./ShowRecoveryCodes");
    }

    private void ClearPassword()
    {
        Input.Password = "";
        ModelState.Remove("Input.Password");
    }

    private async Task LoadQrAsync(ApplicationUser user, string key)
    {
        var formatted = new StringBuilder();
        for (var offset = 0; offset < key.Length; offset += 4)
        {
            if (offset > 0) formatted.Append(' ');
            formatted.Append(key.AsSpan(offset, Math.Min(4, key.Length - offset)));
        }
        SharedKey = formatted.ToString().ToLowerInvariant();
        var uri = string.Format(CultureInfo.InvariantCulture, "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
            encoder.Encode("DRMC Patient Portal"), encoder.Encode(await users.GetEmailAsync(user) ?? ""), key);
        QrCodeSvg = qrCodes.GenerateSvgQrCode(uri);
    }
}
