using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace DrmcPatientPortal.Services;

public sealed class PendingTwoFactorPageFilter(
    SignInManager<ApplicationUser> signInManager,
    ITempDataDictionaryFactory tempDataFactory) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (await signInManager.GetTwoFactorAuthenticationUserAsync() == null)
        {
            tempDataFactory.GetTempData(context.HttpContext)["ErrorMessage"] =
                "Sign in with your email and password before entering a recovery code.";
            context.Result = new RedirectToPageResult("/Account/Login",
                new { area = "Identity", returnUrl = context.HttpContext.Request.Query["returnUrl"].ToString() });
            return;
        }

        await next();
    }
}
