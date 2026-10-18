using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

// ID Document photos are stored strictly outside wwwroot and served exclusively through this authorized endpoint.
[Authorize]
public class PatientDocumentsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLogService;
    private readonly IPatientDocumentStorage _storage;

    public PatientDocumentsController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLogService,
        IPatientDocumentStorage storage)
    {
        _db = db;
        _userManager = userManager;
        _auditLogService = auditLogService;
        _storage = storage;
    }

    [HttpGet]
    public async Task<IActionResult> IdPhoto(int id, string side = "front", bool download = false)
    {
        Response.Headers.CacheControl = "no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
        if (!string.Equals(side, "front", StringComparison.OrdinalIgnoreCase) && !string.Equals(side, "back", StringComparison.OrdinalIgnoreCase)) return NotFound();
        side = side.ToLowerInvariant();

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        var document = await _db.PatientIdDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.PatientUserId == user.Id);

        if (document == null)
        {
            return NotFound();
        }

        string? fileName = side.Equals("back", StringComparison.OrdinalIgnoreCase)
            ? document.BackPhotoFileName
            : document.FrontPhotoFileName;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return NotFound();
        }

        try
        {
            var bytes = await _storage.ReadAsync(user.Id, fileName, document.StorageVersion, HttpContext.RequestAborted);
            var contentType = PatientDocumentStorage.ImageContentType(bytes);
            await _auditLogService.LogAsync(
                user.Id,
                download ? "DOWNLOAD_ID_PHOTO" : "VIEW_ID_PHOTO",
                $"PatientIdDocument/{id}/{side}",
                $"{(download ? "Downloaded" : "Viewed")} government ID photo ({side}).",
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            var extension = contentType switch { "image/png" => "png", "image/webp" => "webp", _ => "jpg" };
            return new PrivateFileResult(bytes, contentType, $"patient-id-{id}-{side}.{extension}", download);
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (DirectoryNotFoundException) { return NotFound(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or AuditLogPersistenceException)
        { return StatusCode(503); }
    }
}
