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
            if (rune.Value is '.' or ',' || IsAccent(rune)) continue;
            result.Append(Rune.ToUpperInvariant(Rune.ToLowerInvariant(rune)).ToString());
        }
        return Clean(result.ToString());
    }

    // Preserve script vowel signs, enclosing marks and variation selectors: they are not accents.
    private static bool IsAccent(Rune rune) => Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark
        && rune.Value != 0x034F // Combining grapheme joiner is a formatting character, not an accent.
        && rune.Value is >= 0x0300 and <= 0x036F or >= 0x1AB0 and <= 0x1AFF
            or >= 0x1DC0 and <= 0x1DFF or >= 0xFE20 and <= 0xFE2F;
}
