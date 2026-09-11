using System.Security.Claims;

namespace DrmcPatientPortal.Middleware;

public sealed class ProtectedResponseCacheMiddleware
{
    private readonly RequestDelegate _next;

    public ProtectedResponseCacheMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsProtectedRequest(context.Request.Path, context.User))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0, must-revalidate";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers.Expires = "0";
                context.Response.Headers.Vary = "Cookie";
                return Task.CompletedTask;
            });
        }

        await _next(context);
    }

    public static bool IsProtectedRequest(PathString path, ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true
           || path.StartsWithSegments("/Patient")
           || path.StartsWithSegments("/Identity/Account/Manage");
}

public static class ProtectedResponseCacheMiddlewareExtensions
{
    public static IApplicationBuilder UseProtectedResponseCacheControls(this IApplicationBuilder app)
        => app.UseMiddleware<ProtectedResponseCacheMiddleware>();
}
