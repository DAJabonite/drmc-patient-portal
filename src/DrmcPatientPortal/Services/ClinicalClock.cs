namespace DrmcPatientPortal.Services;

public static class ClinicalClock
{
    private static readonly TimeZoneInfo Manila = FindManila();
    public static DateTime Today => WallTime(DateTimeOffset.UtcNow).Date;
    public static DateTime WallTime(DateTimeOffset utc) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(utc, Manila).DateTime, DateTimeKind.Unspecified);

    private static TimeZoneInfo FindManila()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time"); }
    }
}
