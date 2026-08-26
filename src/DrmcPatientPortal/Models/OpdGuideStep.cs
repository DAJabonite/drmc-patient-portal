namespace DrmcPatientPortal.Models;

// Static reference data for DRMC's Outpatient Department (OPD) patient journey sequence,
// verified against DRMC Citizen's Charter and DOH Level III hospital standards.
public record OpdGuideStep(
    int StepNumber,
    string Title,
    string Location,
    string? RequirementNote = null
);

public static class OpdGuideSteps
{
    public static readonly IReadOnlyList<OpdGuideStep> All = new List<OpdGuideStep>
    {
        new(
            1,
            "OPD Triage & Screening Desk",
            "OPD Main Entrance (Ground Floor)",
            "Valid ID & referral letter (if any)"
        ),
        new(
            2,
            "HIMD Registration & Records Counter",
            "Health Information Management Dept (Ground Floor)",
            "Patient record / consultation queue number"
        ),
        new(
            3,
            "Nursing Station & Vital Signs Area",
            "OPD Clinic Waiting Area",
            "Blood pressure, temperature, pulse & weight"
        ),
        new(
            4,
            "Specialty Clinic Consultation Room",
            "Assigned Department Clinic (Floors 1–3)",
            "Physician evaluation & prescription orders"
        ),
        new(
            5,
            "Diagnostic & Laboratory Sections",
            "Central Lab (Ground Floor) / Radiology (Basement 1)",
            "Diagnostic blood tests & imaging (if ordered)"
        ),
        new(
            6,
            "OPD Pharmacy Dispensing Window",
            "OPD Pharmacy Window 2 (Ground Floor)",
            "Prescription submission & medicine release"
        )
    };

    public static readonly string IntroLine = "Follow this step-by-step sequence to navigate your outpatient consultation visit at Davao Regional Medical Center.";
    public static readonly string KioskReferralLine = "Once inside the hospital, visit the physical wayfinder kiosk near the main lobby for interactive indoor directions.";
}
