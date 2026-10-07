using System.ComponentModel.DataAnnotations;

namespace DrmcPatientPortal.Models;

// A single-use code handed to a patient in person (PACD or a clinic desk) after staff check their
// identity. It proves the person has a DRMC hospital record and links that record to the portal
// account created with it. Only a SHA-256 hash of the code is stored.
public class PatientRegistrationCode
{
    public const int HintLength = 5, IssuingPointLength = 80;

    public int Id { get; set; }
    public int PatientRecordId { get; set; }
    public PatientRecord PatientRecord { get; set; } = null!;
    public byte[] CodeHash { get; set; } = [];
    public string CodeHint { get; set; } = string.Empty;
    public string IssuingPoint { get; set; } = string.Empty;
    public string IssuedById { get; set; } = string.Empty;
    public string IssuedByEmail { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RedeemedAtUtc { get; set; }
    public string? RedeemedByUserId { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public RegistrationCodeStatus StatusAt(DateTime utcNow) =>
        RedeemedAtUtc is not null ? RegistrationCodeStatus.Redeemed
        : RevokedAtUtc is not null ? RegistrationCodeStatus.Revoked
        : ExpiresAtUtc <= utcNow ? RegistrationCodeStatus.Expired
        : RegistrationCodeStatus.Active;
}

public enum RegistrationCodeStatus { Active, Redeemed, Revoked, Expired }

// Places where staff hand out codes. Red Star Clinic is named by DRMC; the list stays short so
// reports group cleanly.
public static class RegistrationCodeIssuingPoints
{
    public const string Pacd = "PACD (Public Assistance and Complaints Desk)";
    public static readonly IReadOnlyList<string> All =
    [
        Pacd,
        "Red Star Clinic",
        "OPD clinic",
        "Emergency department",
        "Admitting / Inpatient services",
        "Medical Social Service (Malasakit Center)",
    ];
}
