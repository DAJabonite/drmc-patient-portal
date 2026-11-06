using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

[Authorize]
public class PatientController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;
    private readonly PatientReportQueries _reports;

    public PatientController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IAuditLogService auditLog,
        PatientReportQueries reports)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
        _reports = reports;
    }

    public async Task<IActionResult> Home()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var encounters = await SqlReadRetry.RunAsync(_db, () => _db.ClinicalEncounters
            .AsNoTracking()
            .Where(e => e.Patient.PortalUserId == user.Id)
            .OrderByDescending(e => e.EncounterDate)
            .ThenByDescending(e => e.Id).Take(1).ToListAsync());

        var visibleLabs = await _reports.LaboratoryPreviewAsync(user.Id, HttpContext.RequestAborted);

        var activePrescriptions = await SqlReadRetry.RunAsync(_db, () => _db.Prescriptions
            .CountAsync(p => p.Patient.PortalUserId == user.Id && p.Status == PrescriptionStatus.Active));

        var model = new PatientDashboardViewModel
        {
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            ContactNumber = user.ContactNumber,
            CreatedAt = user.CreatedAt,
            Encounters = encounters,
            LabResults = visibleLabs.Reports,
            EncountersCount = await SqlReadRetry.RunAsync(_db, () => _db.ClinicalEncounters.CountAsync(e => e.Patient.PortalUserId == user.Id)),
            LabResultsCount = visibleLabs.Total,
            ActivePrescriptionsCount = activePrescriptions,
            Departments = ClinicalDepartments.All,
        };

        await _auditLog.LogAsync(user.Id, "VIEW_PATIENT_DASHBOARD", "Patient/Home", "Viewed owned clinical previews and counts.", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return View(model);
    }

    // GET /Patient/Audit
    [HttpGet("Patient/Audit")]
    public async Task<IActionResult> Audit()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var logs = await SqlReadRetry.RunAsync(_db, () => _db.AuditLogs
            .Where(a => a.UserId == user.Id)
            .OrderByDescending(a => a.Timestamp)
            .Take(50)
            .ToListAsync());

        var model = new PatientAuditViewModel
        {
            PatientName = user.FullName,
            Email = user.Email ?? string.Empty,
            Logs = logs
        };

        return View(model);
    }
}

public class PatientDashboardViewModel
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public IReadOnlyList<ClinicalEncounter> Encounters { get; set; } = Array.Empty<ClinicalEncounter>();
    public IReadOnlyList<PatientReportCard> LabResults { get; set; } = Array.Empty<PatientReportCard>();
    public int ActivePrescriptionsCount { get; set; }
    public int EncountersCount { get; set; }
    public int LabResultsCount { get; set; }
    public IReadOnlyList<ClinicalDepartment> Departments { get; set; } = Array.Empty<ClinicalDepartment>();
}

public class PatientAuditViewModel
{
    public string PatientName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public IReadOnlyList<AuditLog> Logs { get; set; } = Array.Empty<AuditLog>();
}
