namespace DrmcPatientPortal.Models;

public class LabResult
{
    public int Id { get; set; }
    public string PatientUserId { get; set; } = string.Empty;
    public ApplicationUser Patient { get; set; } = null!;

    public string AccessionNumber { get; set; } = string.Empty; // e.g. "DRMC-LAB-2026-0981"
    public string TestName { get; set; } = string.Empty;        // e.g. "Complete Blood Count (CBC) with Platelet"
    public LabCategory Category { get; set; } = LabCategory.Hematology;
    public DateTime CollectedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public string Status { get; set; } = "Available"; // Available, In progress, Pending Verification
    public string ResultSummary { get; set; } = string.Empty;

    public string OrderingPhysician { get; set; } = string.Empty; // e.g. "Dr. Arthur Llanos, MD, FPCP"
    public string PathologistName { get; set; } = string.Empty;   // e.g. "Dr. Manuel Santos, MD, FPSP"
    public string PerformingUnit { get; set; } = "DRMC Central Clinical Diagnostic Laboratory";
    public string ClinicalNotes { get; set; } = string.Empty;
    public ICollection<LabResultItem> Items { get; set; } = new List<LabResultItem>();
}

public enum LabCategory
{
    Hematology,
    ClinicalChemistry,
    Microbiology,
    UrinalysisFecalysis,
    RadiologyImaging,
    SpecialDiagnostics
}
