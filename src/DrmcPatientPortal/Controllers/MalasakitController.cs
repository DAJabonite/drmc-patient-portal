using System.Text.Json;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

public class MalasakitController : Controller
{
    private readonly ApplicationDbContext _db;

    public MalasakitController(ApplicationDbContext db)
    {
        _db = db;
    }

    // GET /Malasakit
    public async Task<IActionResult> Index()
    {
        var programs = await _db.AssistancePrograms.ToListAsync();
        return View(programs);
    }

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
            EstimatedCoverage = input.PatientCategory == "Indigent" ? "Up to 100% (Zero Balance Billing under RA 11463)" : "PhilHealth + DOH MAIP Co-share Assistance"
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
