using System.Text.RegularExpressions;

namespace DrmcPatientPortal.Services;

/// <summary>Reads printed labels separately from their values, including bilingual labels.</summary>
internal static class IdDocumentLabels
{
    // Longer aliases precede shorter ones so "Gitnang Pangalan" cannot become "Pangalan".
    private static readonly Regex Labels = new(
        @"(?<![\p{L}\p{N}])(?:" +
        @"(?<MiddleName>Middle[ \t]*Name|Gitnang[ \t]+Pangalan)|" +
        @"(?<LastName>Last[ \t]*Name|Surname|Apelyido)|" +
        @"(?<FirstName>First[ \t]*Name|Given[ \t]*Names?|Mga[ \t]+Pangalan|Pangalan)|" +
        @"(?<FullName>Full[ \t]*Name|Member[ \t]*Name|Buong[ \t]+Pangalan|Cardholder|Name)|" +
        @"(?<DateOfBirth>Date[ \t]*of[ \t]*Birth|Birth[ \t]*Date|DOB|Birthday|Petsa[ \t]+ng[ \t]+Kapanganakan)|" +
        @"(?<Sex>Sex|Gender|Kasarian)|" +
        @"(?<BloodType>Blood[ \t]*Type|Blood|Uri[ \t]+ng[ \t]+Dugo)|" +
        @"(?<Address>Address|Residence|Tirahan)|" +
        @"(?<Other>Nationality|Nasyonalidad|Citizenship|Height|Weight|Tangkad|Timbang|" +
        @"Date[ \t]*(?:of[ \t]*)?(?:Issue|Issuance|Expiry|Expiration)|Expiry|Expiration|" +
        @"Signature|Lagda|Passport[ \t]*(?:No\.?|Number)|License[ \t]*(?:No\.?|Number)|" +
        @"ID[ \t]*(?:No\.?|Number)|PCN|PSN|CRN|PIN|Registration[ \t]*No\.?)" +
        @")(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    private static readonly string[] Fields =
        ["MiddleName", "LastName", "FirstName", "FullName", "DateOfBirth", "Sex", "BloodType", "Address", "Other"];

    public static IReadOnlyDictionary<string, string> Read(string text)
    {
        var values = new Dictionary<string, string>();
        string? pending = null;
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var matches = Labels.Matches(line);
            if (matches.Count == 0)
            {
                if (pending is not null) AddValue(pending, line);
                continue;
            }

            // An address may continue on the next line before another field begins.
            if (pending == "Address" && matches[0].Index > 0)
                AddValue(pending, line[..matches[0].Index]);

            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                var field = Fields.First(name => match.Groups[name].Success);
                pending = field == "Other" ? null : field;
                var end = i + 1 < matches.Count ? matches[i + 1].Index : line.Length;
                var value = line[(match.Index + match.Length)..end].Trim(' ', '\t', ':', '.', '/', '|')
                    .TrimStart('-', '–', '—').TrimStart();
                if (pending is not null && value.Length > 0) AddValue(pending, value);
            }
        }
        return values;

        void AddValue(string field, string value)
        {
            value = Regex.Replace(value.Trim(), @"\s+", " ");
            if (value.Length == 0) return;
            if (field == "Address" && values.TryGetValue(field, out var previous))
                values[field] = previous + " " + value;
            else
                values.TryAdd(field, value);
            // Single-line fields stop after their value; addresses continue to the next label.
            if (field != "Address") pending = null;
        }
    }
}
