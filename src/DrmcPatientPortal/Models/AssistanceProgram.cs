namespace DrmcPatientPortal.Models;

public class AssistanceProgram
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty; // "MALASAKIT", "MAIP", "PHILHEALTH", "DSWD_AICS", "PCSO"
    public string Title { get; set; } = string.Empty;
    public string ManagingAgency { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CoverageScope { get; set; } = string.Empty; // Inpatient, Outpatient, Medicines, Diagnostics, Hemodialysis
    public string EligibilitySummary { get; set; } = string.Empty;
    public string RequiredDocumentsJson { get; set; } = string.Empty; // JSON array of required docs
    public string StepByStepProcedureJson { get; set; } = string.Empty; // JSON array of step instructions
    public string OfficeLocation { get; set; } = string.Empty; // "Malasakit Center, Ground Floor, DRMC Main Hospital Building"
    public string OperatingHours { get; set; } = string.Empty; // "Monday to Friday, 7:00 AM - 5:00 PM"
}
