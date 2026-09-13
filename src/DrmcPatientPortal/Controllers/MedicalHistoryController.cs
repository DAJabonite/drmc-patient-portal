using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

[Authorize]
[Route("Patient/MedicalHistory")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MedicalHistoryController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IAuditLogService audit) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? category)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        category = category?.ToUpperInvariant();
        if (category is not null && !MedicalHistoryViewModel.Categories.Contains(category)) return BadRequest();

        var records = await db.ClinicalEncounters.AsNoTracking()
            .Where(e => e.PatientUserId == user.Id).OrderByDescending(e => e.EncounterDate).ToListAsync();
        await audit.LogAsync(user.Id, "VIEW_MEDICAL_HISTORY", "ClinicalEncounter",
            "Viewed owned medical history.", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return View(new MedicalHistoryViewModel { SelectedCategory = category, Records = records });
    }
}

public class MedicalHistoryViewModel
{
    public static readonly string[] Categories = ["OPD", "ER", "ADMITTED"];
    public string? SelectedCategory { get; init; }
    public IReadOnlyList<ClinicalEncounter> Records { get; init; } = [];
    // Teleconsultations are outpatient care, retaining their original encounter label.
    public static string CategoryFor(EncounterType type) => type switch
    {
        EncounterType.Emergency => "ER",
        EncounterType.Inpatient => "ADMITTED",
        _ => "OPD"
    };
}
