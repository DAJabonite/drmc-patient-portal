namespace DrmcPatientPortal.Models;

// SECURITY REVIEW TODO (Item #2): Caregiver & Proxy Access Trust Model
// BOUNDARY NOTE: Dependent profile linking, statutory consent recording, and in-session profile switching are fully built in code.
// Before deployment with real patient data, a security engineer must review:
// 1. Formal identity validation & documentary verification (PSA birth certificate, legal guardianship decree) before proxy activation.
// 2. Automated access expiration or re-consent upon minor reaching age of majority (18 years old in the Philippines).
// 3. Strict logging and separation of proxy vs primary account actions.
// Reference: docs/SECURITY_REVIEW_TODO.md
public class DependentProfile
{
    public int Id { get; set; }
    public string GuardianUserId { get; set; } = string.Empty;
    public ApplicationUser Guardian { get; set; } = null!;

    public string FullName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public RelationshipType Relationship { get; set; } = RelationshipType.Child;

    public string? PhilHealthNumber { get; set; }
    public string? IdType { get; set; }
    public string? IdNumber { get; set; }

    public bool StatutoryConsentAgreed { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum RelationshipType
{
    Child,
    Parent,
    Spouse,
    Sibling,
    LegalWard
}
