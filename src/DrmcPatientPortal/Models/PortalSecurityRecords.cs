namespace DrmcPatientPortal.Models;

public sealed class PortalSession
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
}

public sealed class AuthenticatorConsumption
{
    public string UserId { get; set; } = string.Empty;
    public string KeyFingerprint { get; set; } = string.Empty;
    public long TimeStep { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
}

public enum AccountEmailStatus { Pending, Sending, Delivered, Failed, Expired }

public sealed class AccountEmail
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public byte[]? ProtectedPayload { get; set; }
    public AccountEmailStatus Status { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset NextAttemptUtc { get; set; }
    public int Attempts { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
}
