namespace DrmcPatientPortal.Models;

public enum ImportStatus { Staged, Validated, Queued, Running, Succeeded, Failed, Cancelled, Expired, ValidationQueued, Validating, ApprovalQueued, Approving }

public class ImportBatch
{
    public Guid Id { get; set; }
    public string ActorId { get; set; } = "";
    public string ActorEmail { get; set; } = "";
    public string? ApprovedById { get; set; }
    public string? ApprovedByEmail { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string Template { get; set; } = "";
    public string FileHash { get; set; } = "";
    public string? ValidationHash { get; set; }
    public string? ApprovalHash { get; set; }
    public string ValidationVersion { get; set; } = "";
    public ImportStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int RowCount { get; set; }
    public int CreatedCount { get; set; }
    public bool StagingPurged { get; set; }
    public string? FailureCode { get; set; }
    public string? OwnerId { get; set; }
    public DateTime? LeaseExpiresUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
