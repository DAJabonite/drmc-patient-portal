namespace DrmcPatientPortal.Models;

public class Doctor
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty; // e.g. "Dr. Arthur Llanos"
    public string Title { get; set; } = string.Empty;    // e.g. "MD, FPCP, FPSMO"
    public string Department { get; set; } = string.Empty; // References one of the 8 verified clinical departments
    public string SubSpecialty { get; set; } = string.Empty; // e.g. "Medical Oncology / Adult Medicine"
    public string ClinicRoom { get; set; } = string.Empty; // e.g. "OPD Building 2, Room 204"
    public string ScheduleSummary { get; set; } = string.Empty; // e.g. "Mon / Wed / Fri: 8:00 AM - 12:00 PM"
    public bool OffersTeleconsult { get; set; } = true;
    public string Biography { get; set; } = string.Empty;
    public string PrcLicenseMasked { get; set; } = string.Empty; // e.g. "PRC Lic. No. 009****"
    public bool IsActive { get; set; } = true;

    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
