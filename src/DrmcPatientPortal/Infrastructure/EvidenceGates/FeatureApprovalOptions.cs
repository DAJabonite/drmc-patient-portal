namespace DrmcPatientPortal.Infrastructure.EvidenceGates;

public sealed class FeatureApprovalOptions
{
    public const string SectionName = "FeatureApprovals";

    public Dictionary<string, FeatureApprovalRecord> Features { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FeatureApprovalRecord
{
    public bool Enabled { get; init; }
    public string? EvidenceReference { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public string? Notes { get; init; }
}
