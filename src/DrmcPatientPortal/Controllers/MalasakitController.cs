using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

public class MalasakitController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuditLogService _audit;

    public MalasakitController(ApplicationDbContext db, UserManager<ApplicationUser> users, IAuditLogService audit)
    {
        _db = db;
        _users = users;
        _audit = audit;
    }

    // GET /Malasakit
    public async Task<IActionResult> Index()
    {
        var programs = await _db.AssistancePrograms.ToListAsync();
        return View(programs);
    }

    // GET /Malasakit/DSWDServices
    [HttpGet]
    public IActionResult DSWDServices()
    {
        return View();
    }

    // GET /Malasakit/DRMCMalasakitServices
    [HttpGet]
    public IActionResult DRMCMalasakitServices()
    {
        return View();
    }

    [Authorize, HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Apply()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (await _db.SubsidyApplications.AnyAsync(a => a.PatientUserId == user.Id))
            return RedirectToAction(nameof(Status));
        return View(new SubsidyApplicationInput());
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(SubsidyApplicationInput input)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (await _db.SubsidyApplications.AnyAsync(a => a.PatientUserId == user.Id))
            return RedirectToAction(nameof(Status));
        if (!ModelState.IsValid) return View(input);

        await AuditAsync(user.Id, "CREATE_SUBSIDY_APPLICATION");
        var application = new SubsidyApplication
        {
            PatientUserId = user.Id, Need = input.Need!.Value, ReportedPayment = input.ReportedPayment!.Value,
            GovernmentIdReady = input.GovernmentIdReady, ClinicalDocumentReady = input.ClinicalDocumentReady,
            CostDocumentReady = input.CostDocumentReady, IndigencyDocumentReady = input.IndigencyDocumentReady
        };
        _db.SubsidyApplications.Add(application);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            // Two simultaneous submissions still create only one current application.
            _db.Entry(application).State = EntityState.Detached;
            if (!await _db.SubsidyApplications.AnyAsync(a => a.PatientUserId == user.Id)) throw;
        }
        return RedirectToAction(nameof(Status));
    }

    [Authorize, HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Status()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        var application = await _db.SubsidyApplications.AsNoTracking().SingleOrDefaultAsync(a => a.PatientUserId == user.Id);
        await AuditAsync(user.Id, "VIEW_SUBSIDY_APPLICATION");
        return View(application);
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Requirements(SubsidyRequirementsInput input)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!ModelState.IsValid) return BadRequest();
        var application = await _db.SubsidyApplications.SingleOrDefaultAsync(a => a.PatientUserId == user.Id);
        if (application is null) return NotFound();
        await AuditAsync(user.Id, "UPDATE_SUBSIDY_REQUIREMENTS");
        application.GovernmentIdReady = input.GovernmentIdReady;
        application.ClinicalDocumentReady = input.ClinicalDocumentReady;
        application.CostDocumentReady = input.CostDocumentReady;
        application.IndigencyDocumentReady = input.IndigencyDocumentReady;
        application.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["RequirementsSaved"] = true;
        return RedirectToAction(nameof(Status));
    }

    // GET /Malasakit/MaifipRequirements
    public IActionResult MaifipRequirements()
    {
        return View();
    }

    // GET /Malasakit/Philhealth
    public IActionResult Philhealth()
    {
        return View();
    }

    private Task AuditAsync(string userId, string action) => _audit.LogAsync(userId, action,
        "SubsidyApplication", "Accessed own subsidy application.", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    // GET /Malasakit/Navigator
    [HttpGet]
    public IActionResult Navigator()
    {
        return View(new MalasakitAssessmentInput());
    }

    // POST /Malasakit/Assess
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assess(MalasakitAssessmentInput input)
    {
        var allPrograms = await _db.AssistancePrograms.ToListAsync();
        var matched = new List<AssistanceProgram>();
        var checklist = new HashSet<string>();

        // Always include Malasakit Center unified desk
        var malasakit = allPrograms.FirstOrDefault(p => p.Code == "MALASAKIT");
        if (malasakit is not null) matched.Add(malasakit);

        // Core documents required for any assistance desk
        checklist.Add("Original or Photocopy of Valid Government ID of Patient (PhilSys / PhilHealth / Senior ID / PWD ID)");
        checklist.Add("DRMC Medical Certificate or Clinical Abstract signed by Attending Physician");
        checklist.Add("Barangay Certificate of Indigency (specifying purpose: Medical Assistance)");

        // Program-specific evaluations
        if (input.HasPhilHealth)
        {
            var philhealth = allPrograms.FirstOrDefault(p => p.Code == "PHILHEALTH");
            if (philhealth is not null) matched.Add(philhealth);
            checklist.Add("PhilHealth Member Data Record (MDR) or Member ID Card");
        }

        if (input.NeedsMedicines || input.NeedsDiagnostics || input.PatientCategory == "Indigent" || input.PatientCategory == "SeniorPWD")
        {
            var maip = allPrograms.FirstOrDefault(p => p.Code == "MAIP");
            if (maip is not null && !matched.Contains(maip)) matched.Add(maip);
            checklist.Add("Official DRMC Doctor's Prescription / Laboratory Order with Physician's PRC License Number");
            checklist.Add("Hospital Price Quotation from DRMC Pharmacy / Central Laboratory");
        }

        if (input.NeedsSpecialtyCare || input.NeedsHospitalization)
        {
            var pcso = allPrograms.FirstOrDefault(p => p.Code == "PCSO");
            if (pcso is not null && !matched.Contains(pcso)) matched.Add(pcso);

            var dswd = allPrograms.FirstOrDefault(p => p.Code == "DSWD_AICS");
            if (dswd is not null && !matched.Contains(dswd)) matched.Add(dswd);

            checklist.Add("Official Hospital Statement of Account (SOA) from DRMC Billing Section");
            checklist.Add("Social Case Study Report from LGU Municipal Social Welfare and Development Office (MSWDO)");
        }

        var result = new MalasakitAssessmentResult
        {
            Input = input,
            MatchedPrograms = matched,
            RequiredDocuments = checklist.ToList(),
            EstimatedCoverage = "Eligibility and coverage require assessment by the Malasakit Center."
        };

        return View("Results", result);
    }

    // GET /Malasakit/Program/MAIP or /Malasakit/Program?code=MAIP
    [HttpGet]
    public async Task<IActionResult> Program(string? id, string? code)
    {
        var targetCode = code ?? id;
        if (string.IsNullOrWhiteSpace(targetCode))
        {
            return RedirectToAction(nameof(Index));
        }

        var program = await _db.AssistancePrograms
            .FirstOrDefaultAsync(p => p.Code.ToUpper() == targetCode.ToUpper());

        if (program is null)
        {
            return NotFound();
        }

        return View(program);
    }
}

public class MalasakitAssessmentInput
{
    public string PatientCategory { get; set; } = "Indigent"; // Indigent, LowIncome, SeniorPWD, Employed, Other
    public bool HasPhilHealth { get; set; } = true;
    public bool NeedsMedicines { get; set; } = true;
    public bool NeedsDiagnostics { get; set; } = true;
    public bool NeedsSpecialtyCare { get; set; } = false; // Chemotherapy, Dialysis, Surgery
    public bool NeedsHospitalization { get; set; } = false;
    public bool HasBarangayIndigency { get; set; } = true;
}

public class MalasakitAssessmentResult
{
    public MalasakitAssessmentInput Input { get; set; } = null!;
    public IReadOnlyList<AssistanceProgram> MatchedPrograms { get; set; } = Array.Empty<AssistanceProgram>();
    public IReadOnlyList<string> RequiredDocuments { get; set; } = Array.Empty<string>();
    public string EstimatedCoverage { get; set; } = string.Empty;
}
