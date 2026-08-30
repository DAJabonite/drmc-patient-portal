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
    private readonly IWebHostEnvironment _environment;

    public PatientDocumentsController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLogService,
        IWebHostEnvironment environment)
    {
        _db = db;
        _userManager = userManager;
        _auditLogService = auditLogService;
        _environment = environment;
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

        string storageDir = Path.Combine(_environment.ContentRootPath, "App_Data", "PatientIdDocuments", user.Id);
        string filePath = Path.Combine(storageDir, fileName);

        // Security boundary check: Path traversal prevention
        string canonicalPath = Path.GetFullPath(filePath);
        string canonicalStorageDir = Path.GetFullPath(storageDir);
        if (!canonicalPath.StartsWith(canonicalStorageDir, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(canonicalPath))
        {
            return NotFound();
        }

        string contentType = fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

        await _auditLogService.LogAsync(
            "VIEW_ID_PHOTO",
            $"PatientIdDocument/{id}/{side}",
            $"Viewed government ID photo ({side}) for document type {document.IdType}",
            user.Id,
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
        );

        return PhysicalFile(canonicalPath, contentType);
    }
}
