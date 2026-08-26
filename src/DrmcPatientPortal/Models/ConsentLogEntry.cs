namespace DrmcPatientPortal.Models;

// PROXY CONSENT AUDIT LOG (RA 10173 & DOH COMPLIANCE)
// Tracks statutory consent grant, modification, and revocation events for caregiver/proxy delegations.
public class ConsentLogEntry
{
    public int Id { get; set; }
    public string GuardianUserId { get; set; } = string.Empty;
    public ApplicationUser Guardian { get; set; } = null!;

    public int? DependentId { get; set; }
    public string DependentName { get; set; } = string.Empty;
    public ConsentEventType EventType { get; set; } = ConsentEventType.Granted;
    public string ConsentDeclarationText { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum ConsentEventType
{
    Granted,
    Revoked,
    ProfileSwitched,
    VerificationPending
}
