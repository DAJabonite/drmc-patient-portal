using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Services;

// Configuration section "PatientResults". ShowFullResults (decision D4) defaults to false and must
// stay false in every tracked settings file until DRMC approves showing result values to patients.
// Turn it on per environment with PatientResults__ShowFullResults=true.
public sealed class PatientResultsOptions
{
    public const string SectionName = "PatientResults";
    public bool ShowFullResults { get; set; }
}

// Server-side release rules for patient pages. Times are Manila wall time, as stored.
public static class PatientResultsDisclosure
{
    // A study is released when it has a final (or amended) report whose release time has passed.
    public static bool IsReleased(RadiologyStatus status, DateTime? releasedAt, DateTime manilaNow) =>
        status is RadiologyStatus.Final or RadiologyStatus.Amended
        && releasedAt.HasValue
        && releasedAt.Value <= manilaNow;

    // Laboratory results use the existing "Available" status plus a release time that has passed.
    public static bool IsReleased(LabResult result, DateTime manilaNow) =>
        result.Status == "Available"
        && result.ReleasedAt.HasValue
        && result.ReleasedAt.Value <= manilaNow;

    public static DateTime ManilaNow => ClinicalClock.WallTime(DateTimeOffset.UtcNow);
}
