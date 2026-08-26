namespace DrmcPatientPortal.Models;

public class PatientAllergy
{
    public int Id { get; set; }
    public string PatientUserId { get; set; } = string.Empty;
    public ApplicationUser Patient { get; set; } = null!;

    public string Allergen { get; set; } = string.Empty; // e.g. "Penicillin / Beta-lactams"
    public string Reaction { get; set; } = string.Empty; // e.g. "Urticarial rash, facial swelling"
    public AllergySeverity Severity { get; set; } = AllergySeverity.Moderate; // Mild, Moderate, Severe
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}

public enum AllergySeverity
{
    Mild,
    Moderate,
    Severe
}
