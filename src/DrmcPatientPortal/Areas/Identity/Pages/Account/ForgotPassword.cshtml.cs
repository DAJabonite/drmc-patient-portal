using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account;

[AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ForgotPasswordModel(UserManager<ApplicationUser> users, AccountLinks links,
    IAccountEmailQueue emails, AccountPrivacy privacy, ILogger<ForgotPasswordModel> logger,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public sealed class InputModel
    {
        [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var started = Stopwatch.GetTimestamp();
        var user = await users.FindByEmailAsync(Input.Email);
        if (user is not null && await users.IsEmailConfirmedAsync(user))
        {
            try
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                var url = links.Page(HttpContext, "/Account/ResetPassword", new { area = "Identity", code });
                var email = PortalEmailRenderer.Render(new PortalEmailContent(
                    Subject: localizer["AccountEmail_ResetSubject"].Value,
                    Heading: localizer["AccountEmail_ResetAction"].Value,
                    Preview: localizer["AccountEmail_ResetPreview"].Value,
                    Introduction: localizer["AccountEmail_ResetIntro"].Value,
                    ActionText: localizer["AccountEmail_ResetAction"].Value,
                    ActionUrl: url,
                    Instructions: localizer["AccountEmail_ResetInstructions"].Value,
                    UnexpectedGuidance: localizer["AccountEmail_ResetUnexpected"].Value,
                    Language: CultureInfo.CurrentUICulture.Name,
                    FallbackLabel: localizer["PortalEmail_Fallback"].Value));
                await emails.EnqueueAsync(user.Id, Input.Email, email.Subject, email.HtmlBody, email.PlainTextBody, HttpContext.RequestAborted);
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception error) { logger.LogError("Password reset queue unavailable ({ErrorType}).", error.GetType().Name); }
        }
        await privacy.FinishAsync(started, HttpContext.RequestAborted);
        return RedirectToPage("./ForgotPasswordConfirmation");
    }
}
