namespace DrmcPatientPortal.Models;

public enum AdvisoryCategory
{
    HealthAlert,
    Vaccination,
    HospitalNotice,
    SeasonalHealth,
    Advisory
}

public enum AdvisoryPriority
{
    Normal,
    High,
    Urgent
}

public class PublicAdvisory
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public AdvisoryCategory Category { get; set; } = AdvisoryCategory.Advisory;
    public AdvisoryPriority Priority { get; set; } = AdvisoryPriority.Normal;
    public string Summary { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string IssuingUnit { get; set; } = string.Empty; // e.g. "DRMC Public Health Unit / DOH Region XI"
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveUntil { get; set; }
    public bool IsPinned { get; set; }
    public int ViewCount { get; set; }
}
