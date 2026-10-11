using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account.Manage;

[Authorize]
public sealed class EmailModel(UserManager<ApplicationUser> users, AccountLinks links, IAccountEmailQueue queue, AccountPrivacy privacy) : PageModel
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
        await queue.EnqueueAsync(user.Id, Input.NewEmail, "Confirm your new email address",
            $"Confirm your new email address by <a href='{HtmlEncoder.Default.Encode(url)}'>opening this link</a>.", HttpContext.RequestAborted);
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
        await queue.EnqueueAsync(user.Id, user.Email!, "Confirm your email",
            $"Confirm your email by <a href='{HtmlEncoder.Default.Encode(url)}'>opening this link</a>.", HttpContext.RequestAborted);
        await privacy.FinishAsync(started, HttpContext.RequestAborted);
        StatusMessage = "Verification email queued. Check your email.";
        return RedirectToPage();
    }
}
