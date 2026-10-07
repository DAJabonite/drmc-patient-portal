using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class StaffInvitationInput
{
    [Required(ErrorMessage = "Enter the staff member's work email address."), EmailAddress(ErrorMessage = "Enter a valid email address."),
     StringLength(StaffInvitation.EmailLength), Display(Name = "Work email address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select the staff role to grant."), Display(Name = "Staff role")]
    public string Role { get; set; } = StaffRoles.PatientServices;
}

public sealed record StaffInvitationRow(int Id, string Email, string Role, string Hint, string InvitedBy, DateTime CreatedAt, DateTime ExpiresAt,
    DateTime? AcceptedAt, StaffInvitationStatus Status, string RowVersion);

public sealed record StaffInvitationList(IReadOnlyList<StaffInvitationRow> Rows, string? Search, int Page, int Total)
{
    public int Pages => Math.Max(1, (Total + 24) / 25);
}

// The plaintext code exists only in this one response; it is never stored or put in TempData.
public sealed record StaffInvitationSlip(string Email, string Role, string Code, string SignupUrl, string QrSvg, DateTime ExpiresAt, bool Emailed);
