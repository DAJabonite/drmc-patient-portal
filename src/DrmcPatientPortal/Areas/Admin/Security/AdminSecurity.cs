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

public static class StaffRoles
{
    public const string Admin = "Admin", Lab = "LabStaff", Radiology = "RadiologyStaff", PatientServices = "PatientServicesStaff";
    public static readonly IReadOnlyList<string> All = [Admin, Lab, Radiology, PatientServices];
    // The Staff access screen may grant or revoke only these roles; Admin is never grantable there.
    public static readonly IReadOnlyList<string> Grantable = [Lab, Radiology, PatientServices];
    public static string Label(string role) => role switch
    {
        Lab => "Laboratory staff", Radiology => "Radiology staff", PatientServices => "Patient services staff", _ => role,
    };
}

// Explicit, fail-closed controller-to-policy registry for the Admin area. Any Admin-area
// controller not listed here requires AdminAccess.
public static class AdminPolicies
{
    public const string Admin = "AdminAccess", Lab = "LabStaffAccess", Radiology = "RadiologyStaffAccess", Console = "StaffConsoleAccess",
        PatientServices = "PatientServicesAccess";

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Roles = new Dictionary<string, IReadOnlyList<string>>
    {
        [Admin] = [StaffRoles.Admin],
        [Lab] = [StaffRoles.Admin, StaffRoles.Lab],
        [Radiology] = [StaffRoles.Admin, StaffRoles.Radiology],
        [PatientServices] = [StaffRoles.Admin, StaffRoles.PatientServices],
        [Console] = [StaffRoles.Admin, StaffRoles.Lab, StaffRoles.Radiology, StaffRoles.PatientServices],
    };

    public static readonly IReadOnlyDictionary<string, string> Controllers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Home"] = Console,
        ["LabResults"] = Lab,
        ["LabResultItems"] = Lab,
        ["RadiologyStudies"] = Radiology,
        ["RegistrationCodes"] = PatientServices,
    };

    public static string For(string? controller) => controller is not null && Controllers.TryGetValue(controller, out var policy) ? policy : Admin;

    public static bool Allows(string? controller, IReadOnlySet<string> roles) => Roles[For(controller)].Any(roles.Contains);

    public static void Register(AuthorizationOptions options)
    {
        options.AddPolicy(Admin, policy => policy.RequireAuthenticatedUser().RequireRole(StaffRoles.Admin).AddRequirements(new AdminRequirement()));
        foreach (var name in new[] { Lab, Radiology, PatientServices, Console })
            options.AddPolicy(name, policy => policy.RequireAuthenticatedUser().AddRequirements(new StaffRequirement(Roles[name])));
    }
}

// Staff policies re-check the full bar live on every request: confirmed email, enabled 2FA,
// an amr=mfa sign-in, not locked out and current role membership from the store.
public sealed record StaffRequirement(IReadOnlyList<string> Roles) : IAuthorizationRequirement;

public sealed class StaffAuthorizationHandler(UserManager<ApplicationUser> users) : AuthorizationHandler<StaffRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, StaffRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || !context.User.HasClaim("amr", "mfa")) return;
        var user = await users.GetUserAsync(context.User);
        if (user is null || !user.EmailConfirmed || !user.TwoFactorEnabled || await users.IsLockedOutAsync(user)) return;
        foreach (var role in requirement.Roles)
            if (await users.IsInRoleAsync(user, role)) { context.Succeed(requirement); return; }
    }
}

// Live role set for the signed-in staff member, loaded once per request, used to scope navigation
// and the dashboard. Authorization itself is always enforced by the policies above.
public sealed class AdminAccessScope(UserManager<ApplicationUser> users, IHttpContextAccessor http)
{
    private IReadOnlySet<string>? roles;

    public async Task<IReadOnlySet<string>> RolesAsync()
    {
        if (roles is not null) return roles;
        var principal = http.HttpContext?.User;
        var user = principal is null ? null : await users.GetUserAsync(principal);
        roles = user is null ? new HashSet<string>() : (await users.GetRolesAsync(user)).Where(StaffRoles.All.Contains).ToHashSet();
        return roles;
    }

    public async Task<bool> CanAccessAsync(string controller) => AdminPolicies.Allows(controller, await RolesAsync());
}

public sealed class AdminAreaConvention : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        if (!controller.RouteValues.TryGetValue("area", out var area) || area != "Admin") return;
        var policy = AdminPolicies.For(controller.ControllerName);
        controller.Filters.Add(new AuthorizeFilter(policy));
        controller.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
        controller.Filters.Add(new AdminValidationFilter());
        foreach (var selector in controller.Selectors)
            selector.EndpointMetadata.Add(new AuthorizeAttribute(policy));
        if (controller.Attributes.OfType<IAllowAnonymous>().Any() ||
            controller.Actions.Any(a => a.Attributes.OfType<IAllowAnonymous>().Any()))
            throw new InvalidOperationException("Admin endpoints must not allow anonymous access.");
    }
}

// Startup guard: every Admin-area endpoint must carry its registry policy, only known policy
// names may appear, and anonymous access is never allowed.
public static class AdminEndpointGuard
{
    public static void Validate(IEnumerable<Endpoint> endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            var action = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>();
            if (action is null || !action.RouteValues.TryGetValue("area", out var area) || area != "Admin") continue;
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                throw new InvalidOperationException("Admin endpoints must not allow anonymous access.");
            var data = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            if (data.Count == 0 || data.Any(d => d.Policy is null || !AdminPolicies.Roles.ContainsKey(d.Policy)))
                throw new InvalidOperationException("Admin endpoints must require only known Admin-area authorization policies.");
            var expected = AdminPolicies.For(action.ControllerName);
            if (!data.Any(d => d.Policy == expected))
                throw new InvalidOperationException($"Admin endpoints must require the {expected} authorization policy.");
        }
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
        foreach (var role in StaffRoles.All)
            if (!await roles.RoleExistsAsync(role) && !(await roles.CreateAsync(new IdentityRole(role))).Succeeded)
                throw new InvalidOperationException($"{role} role initialization failed.");
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
