using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DrmcPatientPortal.Services;

public sealed class DeletePersonalDataPageFilter(UserManager<ApplicationUser> userManager) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (HttpMethods.IsPost(context.HttpContext.Request.Method))
        {
            var user = await userManager.GetUserAsync(context.HttpContext.User);
            if (user != null && await userManager.HasPasswordAsync(user) &&
                string.IsNullOrEmpty(context.HttpContext.Request.Form["Input.Password"]))
            {
                context.Result = new ContentResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ContentType = "text/plain; charset=utf-8",
                    Content = "Enter your password on the account deletion page before deleting your account."
                };
                return;
            }
        }

        await next();
    }
}
