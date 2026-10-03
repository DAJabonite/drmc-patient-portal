using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace DrmcPatientPortal.Services;

public sealed class EnabledTwoFactorPageFilter(
    UserManager<ApplicationUser> userManager,
    ITempDataDictionaryFactory tempDataFactory) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var user = await userManager.GetUserAsync(context.HttpContext.User);
        if (user == null)
        {
            context.Result = new ChallengeResult();
            return;
        }

        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            tempDataFactory.GetTempData(context.HttpContext)["StatusMessage"] =
                context.ActionDescriptor.ViewEnginePath == "/Account/Manage/Disable2fa"
                    ? "Two-factor authentication is already disabled."
                    : "Two-factor authentication is not enabled. Set up your authenticator app before generating recovery codes.";
            context.Result = new RedirectToPageResult("/Account/Manage/TwoFactorAuthentication", new { area = "Identity" });
            return;
        }

        await next();
    }
}
