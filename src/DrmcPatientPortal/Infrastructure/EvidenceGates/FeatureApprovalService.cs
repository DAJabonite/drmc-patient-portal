using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Infrastructure.EvidenceGates;

public sealed class FeatureApprovalService : IFeatureApprovalService
{
    private readonly FeatureApprovalOptions _options;
    private readonly IWebHostEnvironment _environment;

    public FeatureApprovalService(
        IOptions<FeatureApprovalOptions> options,
        IWebHostEnvironment environment)
    {
        _options = options.Value;
        _environment = environment;
    }

    public bool IsEnabled(string featureKey) => GetDecision(featureKey).IsEnabled;

    public FeatureApprovalDecision GetDecision(string featureKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureKey);

        if (!_options.Features.TryGetValue(featureKey, out var approval))
        {
            return Disabled(featureKey, "No approval record is configured.");
        }

        if (!approval.Enabled)
        {
            return Disabled(featureKey, "The feature is disabled by its approval record.", approval);
        }

        if (!_environment.IsDevelopment()
            && (string.IsNullOrWhiteSpace(approval.EvidenceReference)
                || string.IsNullOrWhiteSpace(approval.ApprovedBy)
                || approval.ApprovedAt is null))
        {
            return Disabled(featureKey, "Production enablement requires complete approval evidence.", approval);
        }

        return new FeatureApprovalDecision(
            featureKey,
            true,
            "Approved and enabled.",
            approval.EvidenceReference,
            approval.ApprovedBy,
            approval.ApprovedAt);
    }

    private static FeatureApprovalDecision Disabled(
        string featureKey,
        string reason,
        FeatureApprovalRecord? approval = null)
        => new(
            featureKey,
            false,
            reason,
            approval?.EvidenceReference,
            approval?.ApprovedBy,
            approval?.ApprovedAt);
}
