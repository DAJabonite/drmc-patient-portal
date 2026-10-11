using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account;

[AllowAnonymous]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ResendEmailConfirmationModel(
    UserManager<ApplicationUser> users,
    IAccountEmailQueue emailSender,
    AccountLinks links,
    AccountPrivacy privacy,
    IStringLocalizer<SharedResource> localizer,
    ILogger<ResendEmailConfirmationModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public bool RequestSent { get; set; }

    public string ReturnUrl { get; private set; } = string.Empty;

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Verification_EmailRequired")]
        [EmailAddress(ErrorMessage = "Verification_EmailInvalid")]
        [StringLength(256, ErrorMessage = "Verification_EmailInvalid")]
        public string Email { get; set; } = string.Empty;
    }

    public void OnGet(string? returnUrl = null) => SetReturnUrl(returnUrl);

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        SetReturnUrl(returnUrl);
        if (!ModelState.IsValid)
            return Page();

        var started = Stopwatch.GetTimestamp();
        var user = await users.FindByEmailAsync(Input.Email);
        if (user is not null && !await users.IsEmailConfirmedAsync(user))
        {
            var userId = await users.GetUserIdAsync(user);
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var callbackUrl = links.Page(HttpContext, "/Account/ConfirmEmail",
                new { area = "Identity", userId, code, returnUrl = ReturnUrl });

            try
            {
                await emailSender.EnqueueAsync(user.Id, Input.Email, localizer["Verification_EmailSubject"],
                    $"{HtmlEncoder.Default.Encode(localizer["Verification_EmailIntro"])} " +
                    $"<a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>{HtmlEncoder.Default.Encode(localizer["Verification_EmailLinkText"])}</a>.", HttpContext.RequestAborted);
            }
            catch (Exception exception)
            {
                // Delivery errors must not reveal whether the submitted address has an account.
                logger.LogError("Email confirmation queue unavailable ({ErrorType}).", exception.GetType().Name);
            }
        }

        await privacy.FinishAsync(started, HttpContext.RequestAborted);
        RequestSent = true;
        // A redirect avoids sending another email when the feedback page is refreshed.
        return RedirectToPage(new { returnUrl = ReturnUrl });
    }

    private void SetReturnUrl(string? returnUrl) =>
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Content("~/Patient/Home");
}
