namespace DrmcPatientPortal.Models;

// Represents a verified government ID document attached to a patient's record,
// stored securely outside wwwroot and accessible only through authorized clinical/patient endpoints.
public class PatientIdDocument
{
    public int Id { get; set; }

    public string PatientUserId { get; set; } = string.Empty;
    public ApplicationUser? Patient { get; set; }

    public string IdType { get; set; } = string.Empty;

    public string IdNumber { get; set; } = string.Empty;

    // Secure relative file paths inside App_Data/PatientIdDocuments/ (outside wwwroot)
    public string? FrontPhotoFileName { get; set; }
    public string? BackPhotoFileName { get; set; }

    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    // OCR extraction metadata
    public float OcrConfidence { get; set; }
    public bool IsManualEntry { get; set; }
    public string? ExtractedFieldsJson { get; set; }
}
