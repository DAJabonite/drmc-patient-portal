using System.Globalization;
using System.Text;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Services;

public static class PatientNames
{
    // Storage keeps the spelling and punctuation supplied by the patient or staff.
    public static string Clean(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static bool Matches(PatientRecord record, string? lastName, string? firstName, string? middleName, string? suffix)
    {
        try
        {
            // Evaluate every part without exposing which comparison failed.
            return RequiredMatch(record.LastName, lastName) & RequiredMatch(record.FirstName, firstName)
                & OptionalMatch(record.MiddleName, middleName) & OptionalMatch(record.Suffix, suffix);
        }
        catch (ArgumentException)
        {
            // Invalid Unicode cannot establish an identity match.
            return false;
        }
    }

    private static bool RequiredMatch(string? record, string? submitted)
    {
        var expected = ComparisonValue(record);
        var actual = ComparisonValue(submitted);
        return expected.Length > 0 && actual.Length > 0 && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    private static bool OptionalMatch(string? record, string? submitted) =>
        string.IsNullOrWhiteSpace(record) || !string.IsNullOrWhiteSpace(submitted)
            && string.Equals(ComparisonValue(record), ComparisonValue(submitted), StringComparison.OrdinalIgnoreCase);

    private static string ComparisonValue(string? value)
    {
        var result = new StringBuilder();
        foreach (var rune in (value ?? "").Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if (rune.Value is '.' or ',' || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
            result.Append(rune.ToString());
        }
        return Clean(result.ToString());
    }
}
