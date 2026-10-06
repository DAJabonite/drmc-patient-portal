using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account;

[AllowAnonymous]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class RegisterConfirmationModel : PageModel
{
    public string Email { get; private set; } = string.Empty;
    public string ReturnUrl { get; private set; } = string.Empty;

    public IActionResult OnGet(string? email, string? returnUrl = null)
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Content("~/Patient/Home");
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
            return RedirectToPage("./Register", new { returnUrl = ReturnUrl });

        // Display only the address supplied by registration; do not look up public account status.
        Email = email;
        return Page();
    }
}
