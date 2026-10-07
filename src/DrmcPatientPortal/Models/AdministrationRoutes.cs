namespace DrmcPatientPortal.Models;

public static class AdministrationRoutes
{
    public const string Pattern = "^(Oral|Sublingual|Buccal|Rectal|Intramuscular|Intravenous|Subcutaneous|Inhalation|Nasal|Ophthalmic|Otic|Topical|Transdermal|Vaginal)$";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        "Oral", "Sublingual", "Buccal", "Rectal", "Intramuscular", "Intravenous", "Subcutaneous",
        "Inhalation", "Nasal", "Ophthalmic", "Otic", "Topical", "Transdermal", "Vaginal"
    });
}
