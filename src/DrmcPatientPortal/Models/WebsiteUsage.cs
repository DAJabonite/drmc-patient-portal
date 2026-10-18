namespace DrmcPatientPortal.Models;

// Transient queue payload only; never mapped to a table or included in logs.
public sealed record WebsiteUsageEvent(DateTimeOffset OccurredUtc, string Address, string UserAgent, string? AccountId);

// Only aggregate counts survive the daily identifier cleanup. Neither table stores account IDs,
// addresses, user agents, page names, or clinical information.
public sealed class WebsiteUsageDay
{
    public DateOnly Day { get; set; }
    public long EstimatedVisitors { get; set; }
    public long ActivePatients { get; set; }
    public byte[]? ProtectedKey { get; set; }
    public DateTimeOffset CoverageStartedUtc { get; set; }
    public DateTimeOffset LastHeartbeatUtc { get; set; }
    public bool HasGap { get; set; }
}

public enum WebsiteUsageAudience { Visitor, Patient }

public sealed class WebsiteUsageMarker
{
    public DateOnly Day { get; set; }
    public WebsiteUsageAudience Audience { get; set; }
    public string Identifier { get; set; } = "";
}
