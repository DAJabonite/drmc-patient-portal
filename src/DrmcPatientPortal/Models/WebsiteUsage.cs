namespace DrmcPatientPortal.Models;

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
