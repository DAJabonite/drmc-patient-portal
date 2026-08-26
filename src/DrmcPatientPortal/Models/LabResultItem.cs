namespace DrmcPatientPortal.Models;

public class LabResultItem
{
    public int Id { get; set; }
    public int LabResultId { get; set; }
    public LabResult LabResult { get; set; } = null!;

    public string ParameterName { get; set; } = string.Empty; // e.g. "Hemoglobin", "Fasting Blood Sugar"
    public string Value { get; set; } = string.Empty;          // e.g. "14.2", "112"
    public string Unit { get; set; } = string.Empty;           // e.g. "g/dL", "mg/dL"
    public string ReferenceRange { get; set; } = string.Empty; // e.g. "12.0 - 16.0", "70 - 99"
    public LabFlag Flag { get; set; } = LabFlag.Normal;        // Normal, High, Low, Critical
}

public enum LabFlag
{
    Normal,
    High,
    Low,
    Critical
}
