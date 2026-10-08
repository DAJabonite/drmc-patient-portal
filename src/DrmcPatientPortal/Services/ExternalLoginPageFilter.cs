using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace DrmcPatientPortal.Services;

public sealed class ExternalLoginPageFilter(
    SignInManager<ApplicationUser> signInManager,
    ITempDataDictionaryFactory tempDataFactory) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (HttpMethods.IsGet(context.HttpContext.Request.Method) &&
            context.ActionDescriptor.ViewEnginePath == "/Account/Manage/ExternalLogins" &&
            context.HandlerMethod?.Name == "LinkLoginCallback")
        {
            var userId = signInManager.UserManager.GetUserId(context.HttpContext.User);
            if (userId is null || await signInManager.GetExternalLoginInfoAsync(userId) is null)
            {
                tempDataFactory.GetTempData(context.HttpContext)["StatusMessage"] =
                    "The external sign-in request expired. Try linking your account again.";
                context.Result = new RedirectToPageResult("/Account/Manage/ExternalLogins", new { area = "Identity" });
                return;
            }
        }

        if (HttpMethods.IsPost(context.HttpContext.Request.Method))
        {
            var managing = context.ActionDescriptor.ViewEnginePath == "/Account/Manage/ExternalLogins";
            var handler = context.HandlerMethod?.Name;
            string? message = null;
            if ((managing && handler == "LinkLogin") || (!managing && handler == null))
            {
                var provider = context.HttpContext.Request.Form["provider"].ToString();
                var schemes = await signInManager.GetExternalAuthenticationSchemesAsync();
                if (!schemes.Any(scheme => scheme.Name == provider))
                    message = "That external sign-in provider is unavailable. Choose an available provider or sign in with your email and password.";
            }
            else if (managing && handler == "RemoveLogin" &&
                (string.IsNullOrWhiteSpace(context.HttpContext.Request.Form["loginProvider"]) ||
                 string.IsNullOrWhiteSpace(context.HttpContext.Request.Form["providerKey"])))
            {
                message = "Choose a linked external account before removing it.";
            }

            if (message != null)
            {
                tempDataFactory.GetTempData(context.HttpContext)[managing ? "StatusMessage" : "ErrorMessage"] = message;
                context.Result = new RedirectToPageResult(managing ? "/Account/Manage/ExternalLogins" : "/Account/Login",
                    new { area = "Identity" });
                return;
            }
        }

        await next();
    }
}
