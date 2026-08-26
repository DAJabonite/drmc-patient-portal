using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class LoginWith2faModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<LoginWith2faModel> _logger;
    private readonly IAuditLogService _auditLog;

    public LoginWith2faModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<LoginWith2faModel> logger,
        IAuditLogService auditLog)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
        _auditLog = auditLog;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required]
        [StringLength(7, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Text)]
        [Display(Name = "Two-Factor Passcode")]
        public string TwoFactorCode { get; set; } = string.Empty;

        [Display(Name = "Remember this machine")]
        public bool RememberMachine { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(bool rememberMe, string? returnUrl = null)
    {
        // Ensure the user has gone through the username & password screen first
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();

        if (user == null)
        {
            throw new InvalidOperationException($"Unable to load two-factor authentication user.");
        }

        ReturnUrl = returnUrl;
        RememberMe = rememberMe;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(bool rememberMe, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        returnUrl ??= Url.Content("~/Patient/Home");

        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user == null)
        {
            throw new InvalidOperationException($"Unable to load two-factor authentication user.");
        }

        var authenticatorCode = Input.TwoFactorCode.Replace(" ", string.Empty).Replace("-", string.Empty);

        var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(authenticatorCode, rememberMe, Input.RememberMachine);

        var userId = await _userManager.GetUserIdAsync(user);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

        if (result.Succeeded)
        {
            _logger.LogInformation("User with ID '{UserId}' logged in with 2fa.", userId);
            await _auditLog.LogAsync(userId, "2FA_LOGIN_SUCCESS", $"ApplicationUser/{userId}", "Successful login with Two-Factor Authentication verification.", ip);
            return LocalRedirect(returnUrl);
        }
        else if (result.IsLockedOut)
        {
            _logger.LogWarning("User with ID '{UserId}' account locked out.", userId);
            await _auditLog.LogAsync(userId, "2FA_LOGIN_LOCKOUT", $"ApplicationUser/{userId}", "Account locked out due to multiple invalid 2FA attempts.", ip);
            return RedirectToPage("./Lockout");
        }
        else
        {
            _logger.LogWarning("Invalid authenticator code entered for user with ID '{UserId}'.", userId);
            await _auditLog.LogAsync(userId, "2FA_LOGIN_FAILED", $"ApplicationUser/{userId}", "Invalid 2FA verification passcode submitted.", ip);
            ModelState.AddModelError(string.Empty, "Invalid verification passcode. Please check your authenticator app and try again.");
            return Page();
        }
    }
}
