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

namespace DrmcPatientPortal.Areas.Identity.Pages.Account.Manage;

[Authorize]
public sealed class EmailModel(UserManager<ApplicationUser> users, AccountLinks links, IAccountEmailQueue queue, AccountPrivacy privacy,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    public string Email { get; private set; } = string.Empty;
    public bool IsEmailConfirmed { get; private set; }
    [TempData] public string? StatusMessage { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public sealed class InputModel
    {
        [Required, EmailAddress, StringLength(256), Display(Name = "New email address")]
        public string NewEmail { get; set; } = string.Empty;
    }
    private void Load(ApplicationUser user)
    {
        Email = user.Email ?? string.Empty;
        IsEmailConfirmed = user.EmailConfirmed;
    }
    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        Load(user);
        Input.NewEmail = Email;
        return Page();
    }
    public async Task<IActionResult> OnPostChangeEmailAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        Load(user);
        if (!ModelState.IsValid) return Page();
        if (Input.NewEmail == user.Email) { StatusMessage = "Your email is unchanged."; return RedirectToPage(); }
        var token = await users.GenerateChangeEmailTokenAsync(user, Input.NewEmail);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var url = links.Page(HttpContext, "/Account/ConfirmEmailChange", new { area = "Identity", userId = user.Id, email = Input.NewEmail, code });
        var email = PortalEmailRenderer.Render(new PortalEmailContent(
            Subject: localizer["AccountEmail_ChangeSubject"].Value,
            Heading: localizer["AccountEmail_ChangeAction"].Value,
            Preview: localizer["AccountEmail_ChangePreview"].Value,
            Introduction: localizer["AccountEmail_ChangeIntro"].Value,
            ActionText: localizer["AccountEmail_ChangeAction"].Value,
            ActionUrl: url,
            Instructions: localizer["AccountEmail_ChangeInstructions"].Value,
            UnexpectedGuidance: localizer["AccountEmail_ChangeUnexpected"].Value,
            Language: CultureInfo.CurrentUICulture.Name,
            FallbackLabel: localizer["PortalEmail_Fallback"].Value));
        await queue.EnqueueAsync(user.Id, Input.NewEmail, email.Subject, email.HtmlBody, email.PlainTextBody, HttpContext.RequestAborted);
        StatusMessage = "Confirmation link queued. Check your email.";
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostSendVerificationEmailAsync()
    {
        var started = Stopwatch.GetTimestamp();
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var url = links.Page(HttpContext, "/Account/ConfirmEmail", new { area = "Identity", userId = user.Id, code });
        var email = PortalEmailRenderer.Render(new PortalEmailContent(
            Subject: localizer["Verification_EmailSubject"].Value,
            Heading: localizer["Verification_EmailLinkText"].Value,
            Preview: localizer["Verification_EmailPreview"].Value,
            Introduction: localizer["Verification_EmailIntro"].Value,
            ActionText: localizer["Verification_EmailLinkText"].Value,
            ActionUrl: url,
            Instructions: localizer["Verification_EmailInstructions"].Value,
            UnexpectedGuidance: localizer["Verification_EmailUnexpected"].Value,
            Language: CultureInfo.CurrentUICulture.Name,
            FallbackLabel: localizer["PortalEmail_Fallback"].Value));
        await queue.EnqueueAsync(user.Id, user.Email!, email.Subject, email.HtmlBody, email.PlainTextBody, HttpContext.RequestAborted);
        await privacy.FinishAsync(started, HttpContext.RequestAborted);
        StatusMessage = "Verification email queued. Check your email.";
        return RedirectToPage();
    }
}
