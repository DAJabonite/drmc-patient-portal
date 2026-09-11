namespace DrmcPatientPortal.Infrastructure.EvidenceGates;

public interface IFeatureApprovalService
{
    FeatureApprovalDecision GetDecision(string featureKey);
    bool IsEnabled(string featureKey);
}

public sealed record FeatureApprovalDecision(
    string FeatureKey,
    bool IsEnabled,
    string Reason,
    string? EvidenceReference,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt);
