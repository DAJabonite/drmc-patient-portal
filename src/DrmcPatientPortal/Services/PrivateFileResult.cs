using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DrmcPatientPortal.Services;

// Execute after MVC result filters so ResponseCache cannot weaken the download policy.
public sealed class PrivateFileResult(byte[] bytes, string contentType, string displayName, bool download)
    : FileContentResult(bytes, contentType)
{
    public override Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        response.Headers.Pragma = "no-cache";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        var disposition = new ContentDispositionHeaderValue(download ? "attachment" : "inline");
        disposition.SetHttpFileName(displayName);
        response.GetTypedHeaders().ContentDisposition = disposition;
        return base.ExecuteResultAsync(context);
    }
}
