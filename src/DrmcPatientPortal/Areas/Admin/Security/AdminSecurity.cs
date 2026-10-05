using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace DrmcPatientPortal.Areas.Admin.Security;

public sealed class AdminRequirement : IAuthorizationRequirement;

public sealed class AdminAuthorizationHandler(UserManager<ApplicationUser> users)
    : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || !context.User.HasClaim("amr", "mfa")) return;
        var user = await users.GetUserAsync(context.User);
        if (user is not null && user.EmailConfirmed && user.TwoFactorEnabled && !await users.IsLockedOutAsync(user) && await users.IsInRoleAsync(user, "Admin"))
            context.Succeed(requirement);
    }
}

public sealed class AdminAreaConvention : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        if (!controller.RouteValues.TryGetValue("area", out var area) || area != "Admin") return;
        controller.Filters.Add(new AuthorizeFilter("AdminAccess"));
        controller.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
        controller.Filters.Add(new AdminValidationFilter());
        foreach (var selector in controller.Selectors)
            selector.EndpointMetadata.Add(new AuthorizeAttribute("AdminAccess"));
        if (controller.Attributes.OfType<IAllowAnonymous>().Any() ||
            controller.Actions.Any(a => a.Attributes.OfType<IAllowAnonymous>().Any()))
            throw new InvalidOperationException("Admin endpoints must not allow anonymous access.");
    }
}

public sealed class AdminValidationFilter : Microsoft.AspNetCore.Mvc.Filters.ActionFilterAttribute
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        foreach (var (key, entry) in context.ModelState.Where(pair => pair.Value?.Errors.Count > 0).ToArray())
        {
            var attempted = entry!.AttemptedValue;
            if (entry.Errors.Any(error => error.Exception is not null ||
                (!string.IsNullOrEmpty(attempted) && error.ErrorMessage.Contains("'" + attempted + "'", StringComparison.Ordinal))))
            {
                entry.Errors.Clear();
                context.ModelState.AddModelError(key, "Enter a valid value.");
            }
            // Invalid raw values must not be parsed again by checkbox and enum tag helpers.
            context.ModelState.SetModelValue(key, null, null);
        }
    }
}

public sealed class AdminAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult result)
    {
        if (!result.Succeeded && context.User.Identity?.IsAuthenticated == true && context.Request.Path.StartsWithSegments("/Admin"))
        {
            await DenyAsync(context);
            return;
        }
        await fallback.HandleAsync(next, context, policy, result);
    }

    public static async Task DenyAsync(HttpContext context)
    {
        var action = new ActionContext(context, context.GetRouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var view = new ViewResult
        {
            ViewName = "/Views/Shared/AdminAccessDenied.cshtml", StatusCode = StatusCodes.Status403Forbidden,
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        await context.RequestServices.GetRequiredService<IActionResultExecutor<ViewResult>>().ExecuteAsync(action, view);
    }
}

public static class AdminBootstrap
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var enabled = configuration.GetValue<bool>("AdminBootstrap:Enabled");
        if (enabled)
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AdminBootstrap")
                .LogWarning("Admin bootstrap is enabled. Remove AdminBootstrap configuration after initial setup.");
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Admin") && !(await roles.CreateAsync(new IdentityRole("Admin"))).Succeeded)
            throw new InvalidOperationException("Admin role initialization failed.");
        if (!enabled) return;
        var email = configuration["AdminBootstrap:Email"];
        if (string.IsNullOrWhiteSpace(email)) return;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        if (user is null || !user.EmailConfirmed || !user.TwoFactorEnabled || await users.IsLockedOutAsync(user))
            throw new InvalidOperationException("Admin bootstrap requires an existing email-confirmed, 2FA-enabled account that is not locked out.");
        if (!await users.IsInRoleAsync(user, "Admin") && !(await users.AddToRoleAsync(user, "Admin")).Succeeded)
            throw new InvalidOperationException("Admin bootstrap promotion failed.");
    }
}
