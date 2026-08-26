using System.Text.Json;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

// PRE-CONSULTATION SELF-TRIAGE & DIGITAL INTAKE
// BOUNDARY NOTE: Clinical intake responses, 0-10 visual pain scales, and acuity scores are saved to SQLite.
// Real-time synchronization to attending clinician workstations is an infrastructure boundary (logged to ILogger).
// A clinician-side workstation dashboard is out-of-scope for this patient-only portal build.
[Authorize]
[Route("Patient/Triage")]
public class TriageController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;
    private readonly ILogger<TriageController> _logger;

    public TriageController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog,
        ILogger<TriageController> logger)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
        _logger = logger;
    }

    // GET /Patient/Triage/Start/{appointmentId}
    [HttpGet("Start/{appointmentId:int}")]
    public async Task<IActionResult> Start(int appointmentId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.PatientUserId == user.Id);

        if (appointment is null)
        {
            return NotFound();
        }

        // Check if an intake was already submitted
        var existing = await _db.TriageIntakes
            .FirstOrDefaultAsync(t => t.AppointmentId == appointmentId);

        if (existing is not null)
        {
            return RedirectToAction(nameof(Summary), new { id = existing.Id });
        }

        var model = new TriageSubmissionViewModel
        {
            AppointmentId = appointmentId,
            BookingReference = appointment.BookingReference,
            Department = appointment.Department,
            DoctorName = appointment.DoctorName,
            ScheduledAt = appointment.ScheduledAt,
            ChiefComplaint = appointment.ChiefComplaint
        };

        return View(model);
    }

    // POST /Patient/Triage/Submit
    [HttpPost("Submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(TriageSubmissionViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(a => a.Id == model.AppointmentId && a.PatientUserId == user.Id);

        if (appointment is null)
        {
            return NotFound();
        }

        // Emergency red flag safeguard intercept
        if (model.HasChestPain || model.HasSevereBreathingDifficulty || model.HasSuddenNumbness || model.HasUncontrolledBleeding)
        {
            var ipRed = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            await _auditLog.LogAsync(user.Id, "EMERGENCY_RED_FLAG_TRIGGERED", $"Appointment/{appointment.Id}", "Patient reported acute emergency red-flag symptoms.", ipRed);
            return RedirectToAction(nameof(EmergencyWarning));
        }

        var symptomsList = new List<string>();
        if (model.HasFever) symptomsList.Add("Fever / Chills");
        if (model.HasCough) symptomsList.Add("Persistent Cough");
        if (model.HasHeadache) symptomsList.Add("Headache / Dizziness");
        if (model.HasFatigue) symptomsList.Add("Fatigue / Weakness");
        if (model.HasGastrointestinal) symptomsList.Add("Nausea / Abdominal Discomfort");
        if (model.HasJointPain) symptomsList.Add("Joint or Muscle Pain");
        if (!string.IsNullOrWhiteSpace(model.OtherSymptoms)) symptomsList.Add(model.OtherSymptoms.Trim());

        var comorbiditiesList = new List<string>();
        if (model.HasHypertension) comorbiditiesList.Add("Hypertension");
        if (model.HasDiabetes) comorbiditiesList.Add("Diabetes Mellitus");
        if (model.HasAsthma) comorbiditiesList.Add("Asthma / COPD");
        if (model.HasHeartDisease) comorbiditiesList.Add("Cardiovascular Disease");
        if (model.HasKidneyDisease) comorbiditiesList.Add("Chronic Kidney Disease");

        // Determine Acuity
        var acuity = TriageAcuity.Routine;
        if (model.PainScale >= 7 || model.HasFever && model.SymptomDurationDays >= 7)
        {
            acuity = TriageAcuity.Priority;
        }

        var intake = new TriageIntake
        {
            AppointmentId = model.AppointmentId,
            PatientUserId = user.Id,
            SubmittedAt = DateTime.UtcNow,
            ChiefComplaint = string.IsNullOrWhiteSpace(model.ChiefComplaint) ? appointment.ChiefComplaint : model.ChiefComplaint,
            SymptomDurationDays = model.SymptomDurationDays,
            PainScale = model.PainScale,
            SymptomsJson = JsonSerializer.Serialize(symptomsList),
            HasEmergencyRedFlags = false,
            ReportedBloodPressure = model.ReportedBloodPressure,
            ReportedTemperature = model.ReportedTemperature,
            ReportedHeartRate = model.ReportedHeartRate,
            ReportedWeightKg = model.ReportedWeightKg,
            ReportedBloodSugar = model.ReportedBloodSugar,
            ComorbiditiesJson = JsonSerializer.Serialize(comorbiditiesList),
            CurrentMedicationsSummary = model.CurrentMedicationsSummary ?? string.Empty,
            AcuityLevel = acuity,
            TriageNotes = $"Digital pre-intake completed by patient. Acuity: {acuity}. Reported vitals and symptom checklist ready for triage verification."
        };

        _db.TriageIntakes.Add(intake);
        await _db.SaveChangesAsync();

        // CLINICIAN WORKSTATION BOUNDARY LOG
        _logger.LogInformation("[TRIAGE_INTAKE_SYNC] Intake #{IntakeId} for appointment {Reference} synced to {Department} clinician queue. Acuity: {Acuity}",
            intake.Id, appointment.BookingReference, appointment.Department, intake.AcuityLevel);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "SUBMIT_TRIAGE_INTAKE", $"TriageIntake/{intake.Id}", $"Submitted digital self-triage for appointment {appointment.BookingReference}", ip);

        return RedirectToAction(nameof(Summary), new { id = intake.Id });
    }

    // GET /Patient/Triage/Summary/{id}
    [HttpGet("Summary/{id:int}")]
    public async Task<IActionResult> Summary(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var intake = await _db.TriageIntakes
            .Include(t => t.Appointment)
            .FirstOrDefaultAsync(t => t.Id == id && t.PatientUserId == user.Id);

        if (intake is null)
        {
            return NotFound();
        }

        return View(intake);
    }

    // GET /Patient/Triage/EmergencyWarning
    [HttpGet("EmergencyWarning")]
    public IActionResult EmergencyWarning()
    {
        return View();
    }
}

public class TriageSubmissionViewModel
{
    public int AppointmentId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }

    public string ChiefComplaint { get; set; } = string.Empty;
    public int SymptomDurationDays { get; set; } = 1;
    public int PainScale { get; set; } = 0; // 0 to 10

    // Symptoms checkboxes
    public bool HasFever { get; set; }
    public bool HasCough { get; set; }
    public bool HasHeadache { get; set; }
    public bool HasFatigue { get; set; }
    public bool HasGastrointestinal { get; set; }
    public bool HasJointPain { get; set; }
    public string? OtherSymptoms { get; set; }

    // Red Flag safeguards
    public bool HasChestPain { get; set; }
    public bool HasSevereBreathingDifficulty { get; set; }
    public bool HasSuddenNumbness { get; set; }
    public bool HasUncontrolledBleeding { get; set; }

    // Vitals
    public string? ReportedBloodPressure { get; set; }
    public string? ReportedTemperature { get; set; }
    public string? ReportedHeartRate { get; set; }
    public string? ReportedWeightKg { get; set; }
    public string? ReportedBloodSugar { get; set; }

    // Comorbidities
    public bool HasHypertension { get; set; }
    public bool HasDiabetes { get; set; }
    public bool HasAsthma { get; set; }
    public bool HasHeartDisease { get; set; }
    public bool HasKidneyDisease { get; set; }

    public string? CurrentMedicationsSummary { get; set; }
}
