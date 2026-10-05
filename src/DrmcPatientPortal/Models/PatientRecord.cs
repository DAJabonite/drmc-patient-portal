using System.ComponentModel.DataAnnotations;

namespace DrmcPatientPortal.Models;

public class PatientRecord
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public string? HospitalNumber { get; set; }
    public string? PortalUserId { get; set; }
    public ApplicationUser? PortalUser { get; set; }
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
    public ICollection<ClinicalEncounter> Encounters { get; set; } = new List<ClinicalEncounter>();
    public ICollection<LabResult> LabResults { get; set; } = new List<LabResult>();
    public ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
    public ICollection<PatientAllergy> Allergies { get; set; } = new List<PatientAllergy>();
}
