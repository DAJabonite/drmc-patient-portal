namespace DrmcPatientPortal.Models;

public class MedicationDoseSchedule
{
    public int Id { get; set; }
    public int PrescriptionId { get; set; }
    public Prescription Prescription { get; set; } = null!;
    public TimeOnly DoseTime { get; set; }
    public int DisplayOrder { get; set; }
}
