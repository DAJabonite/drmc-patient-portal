namespace DrmcPatientPortal.Areas.Admin.Services;

public static class PendingRequestKeys
{
    public static (int Id, byte[] Version)? Parse(string? value)
    {
        var parts = value?.Split(':', 2);
        return parts is { Length: 2 } && int.TryParse(parts[0], out var id) && id > 0 && AdminAudit.Version(parts[1]) is { } version
            ? (id, version) : null;
    }
    public static string Create(int id, byte[] version) => id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + Convert.ToBase64String(version);
}
