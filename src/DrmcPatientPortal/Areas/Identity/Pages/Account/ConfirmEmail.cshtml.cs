using System.Buffers.Text;
using System.Text;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account;

[AllowAnonymous]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ConfirmEmailModel(UserManager<ApplicationUser> users, StaffInvitationActivator invitations) : PageModel
{
    public bool IsConfirmed { get; private set; }
    public string ReturnUrl { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(string? userId, string? code, string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Content("~/Patient/Home");
        if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(code) && Base64Url.IsValid(code))
        {
            var user = await users.FindByIdAsync(userId);
            if (user is not null)
            {
                var token = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(code));
                var result = await users.ConfirmEmailAsync(user, token);
                IsConfirmed = result.Succeeded;
                // Invited staff who already turned on 2FA get their role now.
                if (IsConfirmed) await invitations.TryActivateAsync(user, HttpContext.RequestAborted);
            }
        }

        if (!IsConfirmed)
            Response.StatusCode = StatusCodes.Status400BadRequest;

        return Page();
    }
}
