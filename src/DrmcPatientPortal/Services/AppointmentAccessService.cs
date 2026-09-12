using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace DrmcPatientPortal.Services;

public interface IAppointmentAccessService
{
    string CreateToken(Models.Appointment appointment);
    bool ValidateToken(Models.Appointment appointment, string? token);
    bool HasCookieAccess(HttpContext context, Models.Appointment appointment);
    void GrantCookieAccess(HttpContext context, Models.Appointment appointment, string token);
    void RevokeCookieAccess(HttpContext context, Models.Appointment appointment);
}

public sealed class AppointmentAccessService(IDataProtectionProvider provider) : IAppointmentAccessService
{
    private readonly IDataProtector _protector = provider.CreateProtector("DRMC.AppointmentAccess.Cookie.v1");

    public string CreateToken(Models.Appointment appointment)
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        appointment.PublicAccessTokenHash = Hash(token);
        var clinicLocalTime = DateTime.SpecifyKind(appointment.ScheduledAt, DateTimeKind.Unspecified);
        appointment.PublicAccessExpiresAt = new DateTimeOffset(clinicLocalTime, TimeSpan.FromHours(8)).AddHours(24).UtcDateTime;
        return token;
    }

    public bool ValidateToken(Models.Appointment appointment, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(appointment.PublicAccessTokenHash) || appointment.PublicAccessExpiresAt <= DateTime.UtcNow)
            return false;
        var expected = Convert.FromHexString(appointment.PublicAccessTokenHash);
        var actual = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public bool HasCookieAccess(HttpContext context, Models.Appointment appointment)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName(appointment.Id), out var protectedToken)) return false;
        try { return ValidateToken(appointment, _protector.Unprotect(protectedToken)); }
        catch (CryptographicException) { return false; }
    }

    public void GrantCookieAccess(HttpContext context, Models.Appointment appointment, string token)
    {
        if (!ValidateToken(appointment, token)) throw new InvalidOperationException("Cannot grant access with an invalid token.");
        context.Response.Cookies.Append(CookieName(appointment.Id), _protector.Protect(token), new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Expires = new DateTimeOffset(DateTime.SpecifyKind(appointment.PublicAccessExpiresAt!.Value, DateTimeKind.Utc)),
            Path = "/Appointments"
        });
    }

    public void RevokeCookieAccess(HttpContext context, Models.Appointment appointment) =>
        context.Response.Cookies.Delete(CookieName(appointment.Id), new CookieOptions { Path = "/Appointments" });

    private static string CookieName(int id) => $"drmc.appointment.access.{id}";
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
