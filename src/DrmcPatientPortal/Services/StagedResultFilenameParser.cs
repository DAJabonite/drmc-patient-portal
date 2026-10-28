using System.Globalization;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Services;

public enum StagedResultKind { Laboratory, Radiology }
public sealed record StagedIdentityCandidate(string HospitalNumber, string PatientFullName);
public sealed record ParsedStagedResult(DateOnly Date, string? TestName, RadiologyModality? Modality,
    IReadOnlyList<StagedIdentityCandidate> IdentityCandidates);

public static partial class StagedResultFilenameParser
{
    public const string GenericLaboratoryName = "Laboratory report";
    [GeneratedRegex(@"(?<=[_-])\d{4}-\d{2}-\d{2}(?=[_-]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex DateToken();

    public static bool TryParse(string fileName, StagedResultKind kind, DateOnly today,
        out ParsedStagedResult? result, out string failureCode)
    {
        result = null; failureCode = "InvalidFilename";
        if (!PrivateStorageRoot.IsLeaf(fileName) || !Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return false;
        var stem = fileName[..^4];
        var dates = DateToken().Matches(stem);
        if (dates.Count != 1) return false;
        var dateToken = dates[0];
        if (!DateOnly.TryParseExact(dateToken.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        { failureCode = "InvalidDate"; return false; }
        if (date > today) { failureCode = "FutureDate"; return false; }
        var prefix = stem[..(dateToken.Index - 1)];
        var suffix = stem[(dateToken.Index + dateToken.Length)..];
        if (suffix.Length > 0) suffix = suffix[1..].Trim();
        if (dateToken.Index + dateToken.Length < stem.Length && suffix.Length == 0) return false;
        RadiologyModality? modality = null;
        if (kind == StagedResultKind.Radiology)
        {
            modality = suffix.ToUpperInvariant() switch
            {
                "XRY" => RadiologyModality.XRay, "MRI" => RadiologyModality.MRI,
                "CT" => RadiologyModality.CT, "UTZ" => RadiologyModality.Ultrasound, _ => null
            };
            if (modality is null) { failureCode = "InvalidModality"; return false; }
        }
        else if (suffix.Length > 200) return false;
        // Do not split all hyphens: both MRNs and actual patient names may contain them.
        var candidates = Enumerable.Range(0, prefix.Length).Where(i => prefix[i] is '_' or '-')
            .Select(i => new StagedIdentityCandidate(prefix[..i].Trim(), prefix[(i + 1)..].Trim()))
            .Where(c => c.HospitalNumber.Length is > 0 and <= 450 && c.PatientFullName.Length is > 0 and <= 255)
            .Distinct().ToArray();
        if (candidates.Length == 0) return false;
        result = new(date, kind == StagedResultKind.Laboratory ? (suffix.Length == 0 ? GenericLaboratoryName : PatientNames.Clean(suffix)) : null,
            modality, candidates);
        failureCode = "";
        return true;
    }
}
