namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed record AdminNavItem(string Controller, string Label, string Icon, string Description);
public sealed record AdminNavSection(string Label, IReadOnlyList<AdminNavItem> Items);

// Single source for the sidebar, breadcrumbs and dashboard module cards.
public static class AdminNavigation
{
    public static readonly IReadOnlyList<AdminNavSection> Sections =
    [
        new("Overview", [new("Home", "Dashboard", "bi-grid-1x2", "Work that needs attention and recent staff activity.")]),
        new("Patient records",
        [
            new("Patients", "Patients", "bi-people", "Hospital registry and verified portal links."),
            new("RegistrationCodes", "Registration codes", "bi-qr-code", "Hospital record codes from PACD or a clinic desk for portal signup."),
            new("ClinicalEncounters", "Encounters", "bi-clipboard2-pulse", "OPD, emergency and inpatient visits."),
            new("LabResults", "Lab results", "bi-droplet", "Result availability and release status."),
            new("LabResultItems", "Lab items", "bi-list-check", "Staff-only analyte values and flags."),
            new("RadiologyStudies", "Radiology", "bi-lungs", "Imaging studies, reports and release status."),
            new("Prescriptions", "Prescriptions", "bi-capsule", "Active and completed medications."),
            new("MedicationDoseSchedules", "Dose schedules", "bi-clock", "Dose timing shown to patients."),
            new("PatientAllergies", "Allergies", "bi-exclamation-triangle", "Recorded allergies and severity."),
        ]),
        new("Public content",
        [
            new("Doctors", "Doctors", "bi-person-badge", "Public doctor and clinic directory."),
            new("PublicAdvisories", "Advisories", "bi-megaphone", "Sanitized public bulletins."),
        ]),
        new("Operations",
        [
            new("Imports", "Imports", "bi-file-earmark-arrow-up", "CSV and Excel batches with review and approval."),
            new("Audit", "Audit history", "bi-journal-text", "Staff access and change history."),
            new("StaffAccess", "Staff access", "bi-person-lock", "Grant or revoke Laboratory, Radiology and Patient services staff roles."),
            new("StaffInvitations", "Staff invitations", "bi-envelope-plus", "One-time signup invitations for staff without a hospital record."),
        ]),
    ];

    // Sections and items the signed-in staff member may open; empty sections are dropped.
    public static IReadOnlyList<AdminNavSection> For(IReadOnlySet<string> roles) => Sections
        .Select(section => section with { Items = section.Items.Where(item => DrmcPatientPortal.Areas.Admin.Security.AdminPolicies.Allows(item.Controller, roles)).ToArray() })
        .Where(section => section.Items.Count > 0).ToArray();

    public static (AdminNavSection Section, AdminNavItem Item)? Find(string? controller)
    {
        foreach (var section in Sections)
            foreach (var item in section.Items)
                if (string.Equals(item.Controller, controller, StringComparison.OrdinalIgnoreCase)) return (section, item);
        return null;
    }
}

public enum ChipTone { Neutral, Success, Warning, Danger, Info }

// Status chips always carry their text, so meaning never depends on colour alone.
public static class AdminChips
{
    public static ChipTone Tone(string? value)
    {
        var v = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return v switch
        {
            "available" or "final" or "linked" or "redeemed" or "activated" or "active" or "succeeded" or "completed" or "normal" or "valid" or "yes" or "mild" => ChipTone.Success,
            "in progress" or "awaiting setup" or "pending verification" or "staged" or "validated" or "queued" or "running" or "validationqueued"
                or "validating" or "approvalqueued" or "approving" or "moderate" or "on hold" or "onhold" => ChipTone.Warning,
            "failed" or "severe" or "critical" or "high" or "low" or "critical high" or "critical low" => ChipTone.Danger,
            "unlinked" or "cancelled" or "expired" or "revoked" or "discontinued" or "inactive" or "no" => ChipTone.Neutral,
            _ => ChipTone.Info,
        };
    }

    public static string Css(string? value) => "admin-chip admin-chip-" + Tone(value).ToString().ToLowerInvariant();

    // Editors summarise rows as "primary | detail | status". Split into a lead text and chips.
    public static (string Lead, IReadOnlyList<string> Chips) Split(string? summary)
    {
        var parts = (summary ?? string.Empty).Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return (string.Empty, []);
        var chips = parts.Skip(1).Where(p => !p.StartsWith("Patient #", StringComparison.Ordinal)).ToArray();
        return (parts[0], chips);
    }

    // Field labels arrive as property names ("ClinicalEncounterId"); show them as sentence-case text.
    public static string Label(string name)
    {
        if (name.Contains(' ')) return name;
        var trimmed = name.EndsWith("Id", StringComparison.Ordinal) && name.Length > 2 ? name[..^2] : name;
        var words = System.Text.RegularExpressions.Regex.Replace(trimmed, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ").ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    // Enum-like values ("SpecialDiagnostics") read better split; free text is left untouched.
    public static string Value(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Z][a-z]+(?:[A-Z][a-z]+)+$")
            ? System.Text.RegularExpressions.Regex.Replace(value, "(?<=[a-z])(?=[A-Z])", " ")
            : value;

    public static string Humanize(Enum value) =>
        System.Text.RegularExpressions.Regex.Replace(value.ToString(), "(?<=[a-z])(?=[A-Z])", " ");
}

public sealed record AdminMetric(string Label, int Value, string Icon, string Controller, string Hint, bool Attention = false);
public sealed record AdminModuleCount(string Controller, int Count);
public sealed record AdminDashboard(
    IReadOnlyList<AdminNavSection> Sections,
    IReadOnlyList<AdminMetric> Attention,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<DrmcPatientPortal.Models.AdminAuditLog> RecentAudit,
    IReadOnlyList<DrmcPatientPortal.Models.ImportBatch> RecentImports);
