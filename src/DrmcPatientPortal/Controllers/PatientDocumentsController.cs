using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

// Security Review Boundary Pointer: docs/SECURITY_REVIEW_TODO.md Item 5
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
    public async Task<IActionResult> IdPhoto(int id, string side = "front")
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        var document = await _db.PatientIdDocuments
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

        var contentType = side.Equals("back", StringComparison.OrdinalIgnoreCase)
            ? document.BackPhotoContentType
            : document.FrontPhotoContentType;
        contentType ??= fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";

        try
        {
            var bytes = await _storage.ReadAsync(user.Id, fileName, document.StorageVersion, HttpContext.RequestAborted);
            await _auditLogService.LogAsync(
                user.Id,
                "VIEW_ID_PHOTO",
                $"PatientIdDocument/{id}/{side}",
                $"Viewed government ID photo ({side}).",
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            if (document.StorageVersion < 2)
            {
                var migrated = await _storage.MigrateLegacyAsync(user.Id, fileName, side, contentType, HttpContext.RequestAborted);
                if (side.Equals("back", StringComparison.OrdinalIgnoreCase))
                {
                    document.BackPhotoFileName = migrated.FileName;
                    document.BackPhotoContentType = migrated.ContentType;
                }
                else
                {
                    document.FrontPhotoFileName = migrated.FileName;
                    document.FrontPhotoContentType = migrated.ContentType;
                }
                document.StorageVersion = migrated.StorageVersion;
                await _db.SaveChangesAsync();
                await _storage.DeleteLegacyAsync(user.Id, fileName, HttpContext.RequestAborted);
            }
            return File(bytes, contentType);
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (InvalidDataException) { return NotFound(); }
    }
}
