using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

[Authorize]
[Route("Patient/LabResults")]
public class LabResultsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public LabResultsController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    // GET /Patient/LabResults
    [HttpGet("")]
    public async Task<IActionResult> Index(LabCategory? category, string? search)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var query = _db.LabResults
            .Include(l => l.Items)
            .Where(l => l.PatientUserId == user.Id)
            .AsQueryable();

        if (category.HasValue)
        {
            query = query.Where(l => l.Category == category.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(l => l.TestName.ToLower().Contains(s) || l.AccessionNumber.ToLower().Contains(s));
        }

        var allCount = await _db.LabResults.CountAsync(l => l.PatientUserId == user.Id);

        var results = await query
            .OrderByDescending(l => l.CollectedAt)
            .ToListAsync();

        var model = new LabResultsIndexViewModel
        {
            SelectedCategory = category,
            SearchTerm = search,
            LabResults = results,
            TotalCount = allCount,
            TotalAvailable = results.Count(r => r.Status == "Available"),
            TotalInProgress = results.Count(r => r.Status != "Available")
        };

        return View(model);
    }

    // GET /Patient/LabResults/Details/{id}
    [HttpGet("Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var result = await _db.LabResults
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == id && l.PatientUserId == user.Id);

        if (result is null)
        {
            return NotFound();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "VIEW_LAB_REPORT", $"LabResult/{id}", "Viewed an owned laboratory report.", ip);

        return View(result);
    }
}

public class LabResultsIndexViewModel
{
    public LabCategory? SelectedCategory { get; set; }
    public string? SearchTerm { get; set; }
    public IReadOnlyList<LabResult> LabResults { get; set; } = Array.Empty<LabResult>();
    public int TotalCount { get; set; }
    public int TotalAvailable { get; set; }
    public int TotalInProgress { get; set; }
}
