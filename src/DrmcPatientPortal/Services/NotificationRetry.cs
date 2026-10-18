namespace DrmcPatientPortal.Services;

public static class NotificationRetry
{
    public static TimeSpan Delay(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(8),
        _ => TimeSpan.FromMinutes(30)
    };
}
