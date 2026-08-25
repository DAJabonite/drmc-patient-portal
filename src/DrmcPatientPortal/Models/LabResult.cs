namespace DrmcPatientPortal.Models;

// Display-only seeded row for the "Recent Lab Results" dashboard card.
public class LabResult
{
    public int Id { get; set; }
    public string PatientUserId { get; set; } = string.Empty;
    public ApplicationUser Patient { get; set; } = null!;

    public string TestName { get; set; } = string.Empty;
    public DateTime CollectedAt { get; set; }
    public string Status { get; set; } = string.Empty; // Plain-language status, e.g., "Available", "In progress"
    public string ResultSummary { get; set; } = string.Empty;
}
