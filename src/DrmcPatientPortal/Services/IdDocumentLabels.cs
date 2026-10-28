using System.Text.RegularExpressions;

namespace DrmcPatientPortal.Services;

/// <summary>Reads printed labels separately from values. OCR substitutions apply to labels only.</summary>
internal static class IdDocumentLabels
{
    // Compound labels precede components: "Gitnang Apelyido" is not a surname.
    private static readonly Regex Labels = new(
        @"(?<![\p{L}\p{N}])(?:" +
        @"(?<Other>Place[ \t]*of[ \t]*Birth|Lugar[ \t]+ng[ \t]+Kapanganakan|" +
        @"Marital[ \t]*Status|Civil[ \t]*Status|Kalagayang[ \t]+\w+|" +
        @"Date[ \t]*(?:of[ \t]*)?(?:Issue|Issuance|Expiry|Expiration)|(?:Expiry|Expiration)[ \t]*Date|" +
        @"Nationality|Nasyonalidad|Citizenship|Height|Weight|Tangkad|Timbang|Signature|Lagda|" +
        @"Digital[ \t]+National[ \t]+[I1]D[ \t]+Number)|" +
        @"(?<MiddleName>M[i1l]?dd[lIie]{1,2}[ \t]*Na(?:m|rn|r)es?|Gitnang[ \t]+(?:Pangalan|Apelyido))|" +
        @"(?<LastName>Las[ti1l][ \t]*Na(?:m|rn|r)es?|Surname|Apelyido)|" +
        @"(?<FirstName>F[i1l]rs[ti1l][ \t]*Na(?:m|rn|r)es?|G[i1lN]v?en[ \t]*Na(?:m|rn|r)es?|Mga[ \t]+Pangalan|Pangalan)|" +
        @"(?<FullName>Full[ \t]*Name|Member[ \t]*Name|Buong[ \t]+Pangalan|Cardholder|Name)|" +
        @"(?<DateOfBirth>Da(?:te|ke|e|[^\p{L}\p{N}\s])[ \t]*of[ \t]*B[iu1¢]r?[tnm]h?|Birth[ \t]*Date|DOB|Birthday|Petsa[ \t]+ng[ \t]+Kapanganakan)|" +
        @"(?<Sex>Sex|Gender|Kasar[ia]an|Kasarion)|" +
        @"(?<BloodType>Blood[ \t]*Type|Blood|Uri[ \t]+ng[ \t]+Dugo)|" +
        @"(?<Address>Add?ress|Addess|Residence|Tirahan|Tratan)|" +
        @"(?<IdNumber>(?:Passport|License|Postal[ \t]*ID|SSS|GSIS|SS|ID|Registration|Reg\.?|Certificate)[ \t]*(?:No\.?|Number|\#)|" +
        @"PCN|PSN|CRN|PIN|PRN|Common[ \t]+Reference[ \t]+Number)" +
        @")(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    private static readonly string[] Fields =
        ["Other", "MiddleName", "LastName", "FirstName", "FullName", "DateOfBirth", "Sex", "BloodType", "Address", "IdNumber"];

    internal sealed record Label(string Field, int Start, int End);
    internal sealed record LtoName(string LastName, string FirstName, string MiddleName, bool ExplicitMiddle);

    internal static IReadOnlyList<Label> Find(string text) => Labels.Matches(text)
        .Select(match => new Label(Fields.First(name => match.Groups[name].Success), match.Index, match.Index + match.Length)).ToArray();

    internal static bool IsLtoNameHeader(string text)
    {
        var labels = Find(text);
        return IsLtoNameLabels(labels) && !Regex.IsMatch(Labels.Replace(text, ""), @"[\p{L}\p{N}]");
    }

    private static bool IsLtoNameLabels(IReadOnlyList<Label> labels) => labels.Count == 3 &&
        labels[0].Field == "LastName" && labels[1].Field == "FirstName" && labels[2].Field == "MiddleName";

    internal static LtoName? ReadLtoName(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++)
        {
            var header = lines[i].Trim();
            var labels = Find(header);
            if (!IsLtoNameLabels(labels)) continue;
            // Only the printed ordered header establishes the one-comma LTO convention.
            if (Regex.IsMatch(Labels.Replace(header[..labels[^1].End], ""), @"[\p{L}\p{N}]")) continue;
            var value = header[labels[^1].End..].Trim(' ', '\t', ':', '.', ',', '|', ';', '-', '–', '—');
            if (value.Length == 0 && i + 1 < lines.Length)
            {
                value = lines[++i].Trim();
                if (Find(value).Count > 0) continue;
            }
            var parts = Regex.Split(value, @"\s*[,;|]+\s*").Select(CleanNameEdges).ToArray();
            if (parts.Length is < 2 or > 3 || !ValidName(parts[0]) || !ValidName(parts[1])) continue;
            if (parts.Length == 3)
            {
                if (parts[2].Length > 0 && !ValidName(parts[2], middle: true)) continue;
                return new(parts[0], parts[1], parts[2], true);
            }
            var given = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return given.Length >= 3
                ? new(parts[0], string.Join(" ", given[..^1]), given[^1], false)
                : new(parts[0], parts[1], "", false);
        }
        return null;

        static bool ValidName(string name, bool middle = false) =>
            TesseractIdDocumentExtractionService.IsName(name) ||
            (middle && Regex.IsMatch(name, @"^\p{L}\.?$"));
        static string CleanNameEdges(string name)
        {
            name = Regex.Replace(name.Trim(), @"\s+", " ");
            return Regex.Replace(name.TrimStart(':', '.', ' '), @"(?:\s+[.:;|]+)+$", "").Trim();
        }
    }

    public static IReadOnlyDictionary<string, string> Read(string text)
    {
        var values = new Dictionary<string, string>();
        string? pending = null;
        string[] columns = [];
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var matches = Find(line);
            if (matches.Count == 0)
            {
                if (columns.Length > 0)
                {
                    var cells = Regex.Split(line, @"\s*[,|]\s*|\s{2,}");
                    if (cells.Length == columns.Length)
                        for (var i = 0; i < cells.Length; i++) AddValue(columns[i], cells[i]);
                    columns = [];
                    pending = null;
                }
                else if (pending is not null) AddValue(pending, line);
                continue;
            }

            columns = [];
            var distinct = matches.Select(m => m.Field).Distinct().ToArray();
            // A row of headers is followed by a row of values, not a value for the last header.
            if (distinct.Length > 1 && matches.All(m => m.Field is "LastName" or "FirstName" or "MiddleName") &&
                !Regex.IsMatch(Labels.Replace(line, ""), @"[\p{L}\p{N}]"))
            {
                columns = distinct;
                pending = null;
                continue;
            }

            if (pending == "Address" && matches[0].Start > 0)
                AddValue(pending, line[..matches[0].Start]);

            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                pending = match.Field == "Other" ? null : match.Field;
                var end = i + 1 < matches.Count ? matches[i + 1].Start : line.Length;
                var value = line[match.End..end].Trim(' ', '\t', ':', '/', '|', ',').TrimStart('.')
                    .TrimStart('-', '–', '—').TrimStart();
                if (pending == "DateOfBirth")
                    value = Regex.Replace(value, @"^[\[(]?\s*Y{2,4}\s*[/.-]\s*M{1,2}\s*[/.-]\s*D{1,2}\s*[\])]?\s*[:.-]?\s*", "", RegexOptions.IgnoreCase);
                if (pending is not null && value.Length > 0) AddValue(pending, value);
            }
        }
        return values;

        void AddValue(string field, string value)
        {
            value = Regex.Replace(value.Trim(), @"\s+", " ");
            if (value.Length == 0) return;
            if (field == "Address" && value.Equals("PHL", StringComparison.OrdinalIgnoreCase)) return;
            if (field == "Address" && values.TryGetValue(field, out var previous))
                values[field] = previous + " " + value;
            else
                values.TryAdd(field, value);
            if (field != "Address") pending = null;
        }
    }
}
