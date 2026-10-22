namespace DrmcPatientPortal.Models;

public sealed class DocumentProtectionState
{
    public int Id { get; set; }
    public int LastDocumentId { get; set; }
    public int Phase { get; set; }
    public string LastOwner { get; set; } = "";
    public string LastFile { get; set; } = "";
    public string? ProtectedCanary { get; set; }
}

public sealed class AccountEmailThrottle
{
    public string RecipientHash { get; set; } = "";
    public DateTimeOffset WindowStartUtc { get; set; }
    public int Attempts { get; set; }
}

public sealed class PrivateFileRetirement
{
    public Guid Id { get; set; }
    public int Kind { get; set; }
    public string Owner { get; set; } = "";
    public string FileName { get; set; } = "";
    public DateTimeOffset FirstUnreferencedUtc { get; set; }
}
