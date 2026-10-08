using System.Buffers.Text;
using System.Text;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DrmcPatientPortal.Services;

public sealed class IdentityLinkPageFilter(UserManager<ApplicationUser> userManager) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var page = context.ActionDescriptor.ViewEnginePath;
        if (HttpMethods.IsGet(request.Method))
        {
            var code = request.Query["code"].ToString();
            if (string.IsNullOrEmpty(code) || !Base64Url.IsValid(code))
            {
                context.Result = InvalidLink();
                return;
            }

            if (page != "/Account/ResetPassword")
            {
                var userId = request.Query["userId"].ToString();
                var email = request.Query["email"].ToString();
                var user = string.IsNullOrEmpty(userId) ? null : await userManager.FindByIdAsync(userId);
                var changingEmail = page == "/Account/ConfirmEmailChange";
                var provider = changingEmail ? userManager.Options.Tokens.ChangeEmailTokenProvider : userManager.Options.Tokens.EmailConfirmationTokenProvider;
                var purpose = changingEmail ? UserManager<ApplicationUser>.GetChangeEmailTokenPurpose(email) : UserManager<ApplicationUser>.ConfirmEmailTokenPurpose;
                var token = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(code));
                if (user == null || (changingEmail && string.IsNullOrWhiteSpace(email)) ||
                    !await userManager.VerifyUserTokenAsync(user, provider, purpose, token))
                {
                    context.Result = InvalidLink();
                    return;
                }
            }
        }
        else if (HttpMethods.IsPost(request.Method) && page == "/Account/ResetPassword" && context.ModelState.IsValid)
        {
            var email = request.Form["Input.Email"].ToString();
            var code = request.Form["Input.Code"].ToString();
            var user = await userManager.FindByEmailAsync(email);
            if (user == null || !await userManager.VerifyUserTokenAsync(user,
                userManager.Options.Tokens.PasswordResetTokenProvider, UserManager<ApplicationUser>.ResetPasswordTokenPurpose, code))
            {
                context.Result = InvalidLink();
                return;
            }
        }

        await next();
    }

    private static ContentResult InvalidLink() => new()
    {
        StatusCode = StatusCodes.Status400BadRequest,
        ContentType = "text/plain; charset=utf-8",
        Content = "Link invalid or expired. Request a new link and try again."
    };
}

public sealed class LocalReturnUrlPageFilter : IPageFilter
{
    public void OnPageHandlerSelected(PageHandlerSelectedContext context) { }

    public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (context.HandlerArguments.TryGetValue("returnUrl", out var value) && value is string returnUrl &&
            !new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(context).IsLocalUrl(returnUrl))
            context.HandlerArguments["returnUrl"] = null;
    }

    public void OnPageHandlerExecuted(PageHandlerExecutedContext context) { }
}
