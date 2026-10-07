using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class RegistrationCodeInput
{
    [Required(ErrorMessage = "Select where the code is being issued."), StringLength(PatientRegistrationCode.IssuingPointLength), Display(Name = "Issuing point")]
    public string IssuingPoint { get; set; } = RegistrationCodeIssuingPoints.Pacd;
    // Checked on the server: the client-side range rule cannot validate a checkbox.
    public bool IdentityVerified { get; set; }
    public const string IdentityError = "Confirm that staff checked the patient's identity.";
}

public sealed record RegistrationCodeRow(int Id, int PatientId, string PatientName, string? HospitalNumber, string Hint, string IssuingPoint,
    DateTime IssuedAt, DateTime ExpiresAt, DateTime? RedeemedAt, RegistrationCodeStatus Status, string RowVersion);

public sealed record RegistrationCodeList(IReadOnlyList<RegistrationCodeRow> Rows, string? Search, int Page, int Total)
{
    public int Pages => Math.Max(1, (Total + 24) / 25);
}

public sealed record RegistrationCodeIssuePage(OwnershipContext Context, RegistrationCodeInput Input, bool Linked, bool HasBirthDate, int ActiveCodes);

// The plaintext code exists only in this one response; it is never stored or put in TempData.
public sealed record RegistrationCodeSlip(OwnershipContext Context, string Code, string SignupUrl, string QrSvg, string IssuingPoint, DateTime ExpiresAt);
