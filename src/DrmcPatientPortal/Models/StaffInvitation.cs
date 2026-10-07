using System.ComponentModel.DataAnnotations;

namespace DrmcPatientPortal.Models;

// A single-use invitation an Admin sends so a DRMC employee can create a staff account without a
// hospital record code. It is bound to one email address and one staff role. Only a SHA-256 hash
// of the code is stored. The role is granted automatically once the new account has a confirmed
// email and two-factor authentication, the same bar Staff access applies to manual grants.
public class StaffInvitation
{
    public const int EmailLength = 256, RoleLength = 64;

    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public byte[] CodeHash { get; set; } = [];
    public string CodeHint { get; set; } = string.Empty;
    public string InvitedById { get; set; } = string.Empty;
    public string InvitedByEmail { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public string? AcceptedByUserId { get; set; }
    public DateTime? RoleGrantedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public StaffInvitationStatus StatusAt(DateTime utcNow) =>
        RoleGrantedAtUtc is not null ? StaffInvitationStatus.Activated
        : RevokedAtUtc is not null ? StaffInvitationStatus.Revoked
        : AcceptedAtUtc is not null ? StaffInvitationStatus.AwaitingSetup
        : ExpiresAtUtc <= utcNow ? StaffInvitationStatus.Expired
        : StaffInvitationStatus.Invited;
}

// Invited: link not used yet. AwaitingSetup: account created, waiting for email confirmation and
// two-factor authentication. Activated: role granted.
public enum StaffInvitationStatus { Invited, AwaitingSetup, Activated, Revoked, Expired }
