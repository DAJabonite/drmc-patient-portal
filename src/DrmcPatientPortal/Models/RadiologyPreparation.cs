namespace DrmcPatientPortal.Models;

public sealed record RadiologyPreparationGuide(string Key, string Title, string Icon, IReadOnlyList<string> Items);

// Compatibility API for the bundled English guidance. Icons remain template-controlled.
public static class RadiologyPreparation
{
    public static readonly IReadOnlyList<string> GeneralItems =
        Services.InstitutionalGuideDefaults.Get("radiology-preparation").Radiology!.GeneralItems;

    public static readonly IReadOnlyList<string> SafetyNotices =
        Services.InstitutionalGuideDefaults.Get("radiology-preparation").Radiology!.SafetyNotices;

    public static readonly IReadOnlyList<RadiologyPreparationGuide> Guides =
        Services.InstitutionalGuideDefaults.Get("radiology-preparation").Radiology!.Guides
            .Select(guide => new RadiologyPreparationGuide(guide.Key, guide.Title, IconFor(guide.Key), guide.Items)).ToArray();

    public static string IconFor(string key) => key switch
    {
        "xray" => "bi-person-bounding-box",
        "ultrasound" => "bi-soundwave",
        "ct" => "bi-circle-square",
        "mri" => "bi-magnet",
        "mammography" => "bi-gender-female",
        _ => throw new ArgumentException("Unknown imaging study type.", nameof(key))
    };
}
