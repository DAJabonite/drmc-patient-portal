using System.Security.Cryptography;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace DrmcPatientPortal.Services;

public enum PatientPdfSource { Laboratory, Radiology, RedStar }

// A verified report can be opened once by the same account, with the normal private PDF response headers.
public sealed class PatientPdfDelivery : IDisposable
{
    public const string RouteName = "PatientPdfFile";
    private const string PdfContentType = "application/pdf";
    private readonly MemoryCache pending = new(new MemoryCacheOptions { SizeLimit = 64 * 1024 * 1024 });
    private readonly object gate = new();

    public static bool WantsJson(HttpRequest request) => request.Headers.Accept == "application/json";

    public IActionResult Deliver(Controller controller, ApplicationUser user, PatientPdfSource source, int id,
        string storedFileName, byte[] bytes, string displayName, bool download)
    {
        var file = new PrivateFileResult(bytes, PdfContentType, displayName, download);
        if (!WantsJson(controller.Request)) return file;

        var ticket = RandomNumberGenerator.GetHexString(64);
        var url = controller.Url.RouteUrl(RouteName, new { ticket });
        if (url is null) return controller.StatusCode(StatusCodes.Status503ServiceUnavailable);

        lock (gate)
        {
            var report = new Report(source, id, storedFileName, file);
            pending.Set(ticket, new PendingPdf(user.Id, user.SecurityStamp, report), new MemoryCacheEntryOptions
            {
                Size = Math.Max(bytes.LongLength, 1024),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            });
            if (!pending.TryGetValue(ticket, out _)) return controller.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return controller.Json(new { url });
    }

    public Report? Take(string ticket, ApplicationUser user)
    {
        if (ticket.Length != 64) return null;
        lock (gate)
        {
            if (!pending.TryGetValue<PendingPdf>(ticket, out var report) || report is null ||
                report.UserId != user.Id || report.SecurityStamp != user.SecurityStamp) return null;
            pending.Remove(ticket);
            return report.Value;
        }
    }

    public void Dispose() => pending.Dispose();

    public sealed record Report(PatientPdfSource Source, int Id, string StoredFileName, PrivateFileResult File);
    private sealed record PendingPdf(string UserId, string? SecurityStamp, Report Value);
}
