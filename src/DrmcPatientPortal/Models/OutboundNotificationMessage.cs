namespace DrmcPatientPortal.Models;

public enum OutboundNotificationChannel { Email, Sms }
public enum OutboundNotificationStatus { Pending, Sending, Delivered, DeadLetter, Expired, Cancelled }

// Addresses, subjects and content exist only in the protected transport envelope.
public sealed class OutboundNotificationMessage
{
    public Guid Id { get; set; }
    public OutboundNotificationChannel Channel { get; set; }
    public byte[]? PayloadProtected { get; set; }
    public OutboundNotificationStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public string? LastError { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public int? StaffInvitationId { get; set; }
}
