using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed partial class WebsiteUsageMiddleware(RequestDelegate next)
{
    [GeneratedRegex("bot|crawler|spider|slurp|headless|uptime|monitor|healthcheck", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Automation();

    public static bool Eligible(HttpContext context)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (action is null || (action.RouteValues.TryGetValue("area", out var area) && !string.IsNullOrEmpty(area)) ||
            !HttpMethods.IsGet(context.Request.Method) || context.Response.StatusCode != StatusCodes.Status200OK ||
            context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) != true ||
            context.Request.Headers["DNT"] == "1" || context.Request.Headers["Sec-GPC"] == "1" ||
            context.Request.Headers["Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Headers["Sec-Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase)) return false;
        // Explicit controller allowlist excludes future APIs/downloads by default. Home errors
        // and status pages are also excluded even if a future handler accidentally returns 200.
        if (action.ControllerName == "Home" && action.ActionName is not ("Index" or "Privacy" or "OpdGuide")) return false;
        if (action.ControllerName is not ("Home" or "Directory" or "Advisories" or "Malasakit" or "Patient" or
            "Visits" or "Medications" or "LabResults" or "Radiology" or "RedStarPatient")) return false;
        var agent = context.Request.Headers.UserAgent.ToString();
        return context.Connection.RemoteIpAddress is not null && agent.Length is > 0 and <= 1024 && !Automation().IsMatch(agent);
    }

    public async Task InvokeAsync(HttpContext context, WebsiteUsageCollector collector, IOptions<WebsiteUsageOptions> options)
    {
        await next(context);
        if (options.Value.Enabled && Eligible(context)) await collector.CollectAsync(context);
    }
}

public sealed class WebsiteUsageCollector(WebsiteUsageStore store, TimeProvider clock, WebsiteUsageHealth health,
    ILogger<WebsiteUsageCollector> logger)
{
    public async Task CollectAsync(HttpContext context)
    {
        var now = clock.GetUtcNow();
        try
        {
            var account = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
            await store.RecordAsync(now, context.Connection.RemoteIpAddress!.MapToIPv6().ToString(),
                context.Request.Headers.UserAgent.ToString(), account, CancellationToken.None);
        }
        catch (Exception error)
        {
            health.Failed(now);
            // Never log the exception object: SQL/crypto errors can contain identifiers or values.
            logger.LogWarning("Website usage collection failed ({FailureType}).", error.GetType().Name);
        }
    }
}

public sealed class WebsiteUsageWorker(WebsiteUsageStore store, TimeProvider clock, IOptions<WebsiteUsageOptions> options,
    WebsiteUsageHealth health, ILogger<WebsiteUsageWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextMaintenance = DateTimeOffset.MinValue;
        var nextCleanup = DateTimeOffset.MinValue;
        var retry = false;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = clock.GetUtcNow();
                if (now >= nextMaintenance)
                {
                    var failure = health.TakeFailure();
                    try { await store.MaintainAsync(now, options.Value.Enabled, failure, stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        if (failure.HasValue) health.Failed(failure.Value);
                        throw;
                    }
                    catch (Exception error)
                    {
                        health.Failed(failure ?? now);
                        logger.LogWarning("Website usage maintenance failed ({FailureType}).", error.GetType().Name);
                    }
                    nextMaintenance = now.AddMinutes(1);
                }
                if (now >= nextCleanup)
                {
                    try
                    {
                        await store.PruneAsync(now, stoppingToken);
                        nextCleanup = WebsiteUsageClock.NextCleanup(now);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    {
                        health.Failed(now);
                        nextCleanup = now.AddMinutes(1);
                        logger.LogWarning("Website usage cleanup failed ({FailureType}).", error.GetType().Name);
                    }
                }
                if (!retry) await store.WaitForFlushAsync(stoppingToken);
                try
                {
                    await store.FlushAsync(stoppingToken);
                    retry = false;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    logger.LogWarning("Website usage flush failed ({FailureType}).", error.GetType().Name);
                    retry = true;
                    await Task.Delay(WebsiteUsageStore.FlushInterval, clock, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        store.Complete();
        try
        {
            await base.StopAsync(cancellationToken);
            while (true)
            {
                try { await store.FlushAsync(cancellationToken); return; }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    logger.LogWarning("Website usage shutdown flush failed ({FailureType}).", error.GetType().Name);
                    await Task.Delay(WebsiteUsageStore.FlushInterval, clock, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            health.Failed(clock.GetUtcNow());
            logger.LogWarning("Website usage shutdown flush did not finish before cancellation.");
        }
    }
}
