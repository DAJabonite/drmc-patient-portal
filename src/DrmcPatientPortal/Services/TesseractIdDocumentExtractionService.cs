using System.Globalization;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Services;

public class TesseractIdDocumentExtractionService : IIdDocumentExtractionService
{
    private readonly OcrRuntime _runtime;

    public TesseractIdDocumentExtractionService(
        OcrRuntime runtime,
        ILogger<TesseractIdDocumentExtractionService> logger)
    {
        _runtime = runtime;
    }

    public Task<IdExtractionResult> ExtractFromBytesAsync(string idType, byte[] frontImageBytes, byte[]? backImageBytes = null) =>
        ExtractFromBytesAsync(idType, frontImageBytes, backImageBytes, CancellationToken.None);

    public async Task<IdExtractionResult> ExtractFromBytesAsync(string idType, byte[] frontImageBytes, byte[]? backImageBytes,
        CancellationToken cancellationToken)
    {
        if (frontImageBytes is null || frontImageBytes.Length is 0 or > IdImageValidation.MaximumBytes ||
            backImageBytes?.Length > IdImageValidation.MaximumBytes)
            throw new InvalidDataException("Each ID photo must be between 1 byte and 5 MB.");
        using var front = new MemoryStream(frontImageBytes, writable: false);
        using var back = backImageBytes is null or { Length: 0 } ? null : new MemoryStream(backImageBytes, writable: false);
        return await ExtractFromStreamsAsync(idType, front, back, cancellationToken);
    }

    public Task<IdExtractionResult> ExtractFromStreamsAsync(string idType, Stream frontImageStream, Stream? backImageStream = null) =>
        ExtractFromStreamsAsync(idType, frontImageStream, backImageStream, CancellationToken.None);

    public async Task<IdExtractionResult> ExtractFromStreamsAsync(string idType, Stream frontImageStream, Stream? backImageStream,
        CancellationToken cancellationToken)
    {
        var job = await _runtime.ProcessAsync(frontImageStream, backImageStream, cancellationToken: cancellationToken, idType: idType);
        var response = job.Response;
        var result = new IdExtractionResult
        {
            IdType = idType,
            RawTextFront = response.FrontText,
            RawTextBack = response.BackText,
            MeanConfidence = backImageStream is null ? response.FrontConfidence : (response.FrontConfidence + response.BackConfidence) / 2
        };
        return response.Extraction ?? result;
    }

    internal static void ParseStructuredFields(IdExtractionResult result, string idType, string frontText, string backText)
    {
        var idConfig = PhilippineIdTypes.GetByName(idType);
        if (idConfig == null)
        {
            // Default loose extraction
            ParseGenericFields(result, frontText);
            result.Success = result.FoundFieldsSummary.Count > 0;
            return;
        }

        switch (idConfig.Name)
        {
            case PhilippineIdTypes.Passport:
                ParsePassport(result, frontText);
                break;

            case PhilippineIdTypes.DriversLicense:
                ParseDriversLicense(result, frontText);
                break;

            case PhilippineIdTypes.PhilSys:
                ParsePhilSys(result, frontText, backText);
                break;

            case PhilippineIdTypes.Umid:
                ParseUmid(result, frontText);
                break;

            case PhilippineIdTypes.PostalId:
                ParsePostalId(result, frontText);
                break;

            case PhilippineIdTypes.PhilHealth:
                ParsePhilHealth(result, frontText);
                break;

            case PhilippineIdTypes.SssGsis:
                ParseSssGsis(result, frontText);
                break;

            case PhilippineIdTypes.PrcId:
                ParsePrcId(result, frontText);
                break;

            default:
                ParseGenericFields(result, frontText);
                break;
        }
        // Capabilities describe what is normally printed, not a prohibition on reading an
        // additional labelled field on a newer card version. Keep document-number rules strict.
        if (!result.DateOfBirth.Found) ExtractDobByLabels(result, frontText);
        if (!result.Sex.Found) ExtractSexByLabels(result, frontText);
        if (!result.BloodType.Found) ExtractBloodTypeByLabels(result, frontText);
        if (!result.Address.Found) ExtractAddressByLabels(result, frontText);
        result.Success = result.FoundFieldsSummary.Count > 0;
    }

    #region Specific ID Parsers

    private static void ParsePassport(IdExtractionResult result, string text)
    {
        ExtractNameByLabels(result, text);
        // Require the number label and a digit: the document title is not a number.
        ExtractIdNumberByPattern(result, text, @"Passport\s*(?:No\.?|Number|\#)[:\.\s]*((?=[A-Z0-9]*\d)[A-Z0-9]{8,10})\b");
        ExtractDobByLabels(result, text);
        ExtractSexByLabels(result, text);
        // 1. Attempt Machine Readable Zone (MRZ) 2-line ICAO 9303 Type P Parsing
        // Line 1: P<PHL<SURNAME<<GIVEN<NAMES<<<<<<<<<<<<
        // Line 2: PASSPORT_NO<PHLYYMMDD<SEX<YYMMDD<...
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => Regex.Replace(l.Trim().ToUpperInvariant(), @"\s+", ""))
                        .Where(l => l.Length == 44 && Regex.IsMatch(l, @"^[A-Z0-9<]+$"))
                        .ToList();

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.StartsWith("P<PHL") || line.StartsWith("P<") || line.StartsWith("P0PHL") || line.StartsWith("POPHL"))
            {
                // MRZ Line 1 found
                string mrzLine1 = line;
                string namePortion = mrzLine1.Length > 5 ? mrzLine1[5..] : "";
                var nameParts = namePortion.Split(new[] { "<<" }, StringSplitOptions.None);
                if (nameParts.Length >= 1)
                {
                    string surname = CleanMrzString(nameParts[0]);
                    if (!result.LastName.Found && IsName(surname))
                    {
                        result.LastName = ExtractedField<string>.From(surname);
                    }
                }
                if (nameParts.Length >= 2)
                {
                    string givenPortion = CleanMrzString(nameParts[1]).Replace('<', ' ').Trim();
                    if (!result.FirstName.Found && IsName(givenPortion))
                        result.FirstName = ExtractedField<string>.From(givenPortion);
                }

                // Check subsequent line for MRZ Line 2
                if (i + 1 < lines.Count)
                {
                    string mrzLine2 = lines[i + 1];
                    if (mrzLine2.Length >= 28)
                    {
                        // Passport Number: First ~9 chars
                        string rawDocNum = mrzLine2.Substring(0, Math.Min(9, mrzLine2.Length)).Replace("<", "").Trim();
                        if (!result.IdNumber.Found && rawDocNum.Length >= 6 && CheckMrzDigit(mrzLine2[..9], mrzLine2[9]))
                        {
                            result.IdNumber = ExtractedField<string>.From(rawDocNum);
                        }

                        // DOB: chars 13..19 (YYMMDD)
                        if (mrzLine2.Length >= 20)
                        {
                            string rawDob = mrzLine2.Substring(13, 6);
                            if (!result.DateOfBirth.Found && Regex.IsMatch(rawDob, @"^\d{6}$") &&
                                CheckMrzDigit(rawDob, mrzLine2[19]))
                            {
                                if (TryParseMrzDate(rawDob, isDob: true, out DateTime dob) && dob.Date <= DateTime.UtcNow.Date)
                                {
                                    result.DateOfBirth = ExtractedField<DateTime>.From(dob);
                                }
                            }

                            // Sex: char 20 ('M', 'F', '<')
                            char sexChar = mrzLine2[20];
                            if (sexChar == 'M') result.Sex = ExtractedField<string>.From("Male");
                            else if (sexChar == 'F') result.Sex = ExtractedField<string>.From("Female");
                        }
                    }
                }

                PopulateFullName(result);
                return;
            }
        }

        // Fallback: Label-anchored parsing if MRZ was unreadable or cropped
        ExtractNameByLabels(result, text);
        ExtractIdNumberByPattern(result, text, @"Passport\s*(?:No\.?|Number|\#)[:\.\s]*((?=[A-Z0-9]*\d)[A-Z0-9]{8,10})\b");
        ExtractDobByLabels(result, text);
        ExtractSexByLabels(result, text);
        PopulateFullName(result);
    }

    private static void ParseDriversLicense(IdExtractionResult result, string text)
    {
        // 1. License Number
        var licMatch = Regex.Match(text, @"(?:License\s*(?:No|Number|\#)?[:\.\s]*|DL\s*No[:\.\s]*|NO\.?[:\.\s]*)([A-Z0-9]{3}[-\s]?\d{2}[-\s]?\d{6}|[A-Z]\d{2}[-\s]?\d{2}[-\s]?\d{6})", RegexOptions.IgnoreCase);
        if (licMatch.Success)
        {
            result.IdNumber = ExtractedField<string>.From(licMatch.Groups[1].Value.Replace(" ", "-").Trim());
        }
        else
        {
            // Generic 11-char pattern for LTO: e.g. D02-18-012345
            var genericMatch = Regex.Match(text, @"\b([A-Z]\d{2}-\d{2}-\d{6})\b");
            if (genericMatch.Success)
            {
                result.IdNumber = ExtractedField<string>.From(genericMatch.Groups[1].Value.Trim());
            }
        }

        // 2. Names (Last Name, First Name, Middle Name)
        ExtractNameByLabels(result, text);
        if (IdDocumentLabels.ReadLtoName(text) is { } name)
        {
            result.LastName = ExtractedField<string>.From(CleanNameString(name.LastName));
            result.FirstName = ExtractedField<string>.From(CleanNameString(name.FirstName));
            result.MiddleName = name.MiddleName.Length == 0 ? ExtractedField<string>.Empty()
                : ExtractedField<string>.From(CleanNameString(name.MiddleName));
            result.NameEvidence = name.ExplicitMiddle ? IdNameEvidence.ExplicitComponents : IdNameEvidence.LtoCombinedRow;
        }

        // 3. Date of Birth
        ExtractDobByLabels(result, text);

        // 4. Sex / Gender
        ExtractSexByLabels(result, text);

        // 5. Blood Type
        ExtractBloodTypeByLabels(result, text);

        // 6. Address
        ExtractAddressByLabels(result, text);

        PopulateFullName(result);
    }

    private static void ParsePhilSys(IdExtractionResult result, string frontText, string backText)
    {
        // Front: PCN/PhilSys Number, Name, DOB, Address
        var pcnMatch = Regex.Match(frontText, @"\b(\d{4}[-\s]\d{4}[-\s]\d{4}[-\s]\d{4})\b");
        if (pcnMatch.Success)
        {
            result.IdNumber = ExtractedField<string>.From(pcnMatch.Groups[1].Value.Replace(" ", "-").Trim());
        }
        else
        {
            var pcnDigits = Regex.Match(frontText, @"\b(\d{16})\b");
            if (pcnDigits.Success)
            {
                result.IdNumber = ExtractedField<string>.From(pcnDigits.Groups[1].Value.Trim());
            }
        }

        ExtractNameByLabels(result, frontText);

        ExtractDobByLabels(result, frontText);
        ExtractAddressByLabels(result, frontText);

        // Back: Sex & Blood Type
        string combinedBack = frontText + "\n" + backText;
        ExtractSexByLabels(result, combinedBack);
        ExtractBloodTypeByLabels(result, combinedBack);

        PopulateFullName(result);
    }

    private static void ParseUmid(IdExtractionResult result, string text)
    {
        // CRN: 0000-0000000-0
        var crnMatch = Regex.Match(text, @"(?:CRN|Common\s*Reference\s*Number)?[:\.\s]*\b(\d{4}[-\s]?\d{7}[-\s]?\d{1})\b", RegexOptions.IgnoreCase);
        if (crnMatch.Success)
        {
            result.IdNumber = ExtractedField<string>.From(crnMatch.Groups[1].Value.Replace(" ", "-").Trim());
        }

        ExtractNameByLabels(result, text);
        ExtractDobByLabels(result, text);
        ExtractAddressByLabels(result, text);
        ExtractSexByLabels(result, text);
        PopulateFullName(result);
    }

    private static void ParsePostalId(IdExtractionResult result, string text)
    {
        // Postal ID Reference Number
        if (IdDocumentLabels.Read(text).TryGetValue("IdNumber", out var postalNumber) &&
            Regex.IsMatch(postalNumber, @"^[A-Z0-9][A-Z0-9-]{5,29}$", RegexOptions.IgnoreCase) && postalNumber.Any(char.IsDigit))
            result.IdNumber = ExtractedField<string>.From(postalNumber);

        ExtractNameByLabels(result, text);
        ExtractDobByLabels(result, text);
        ExtractAddressByLabels(result, text);
        PopulateFullName(result);
    }

    private static void ParsePhilHealth(IdExtractionResult result, string text)
    {
        // PhilHealth Identification Number (PIN): 12 digits or 00-000000000-0
        var pinMatch = Regex.Match(text, @"\b(\d{2}[-\s]?\d{9}[-\s]?\d{1})\b");
        if (pinMatch.Success)
        {
            result.IdNumber = ExtractedField<string>.From(pinMatch.Groups[1].Value.Replace(" ", "-").Trim());
        }
        else
        {
            var pinDigits = Regex.Match(text, @"\b(\d{12})\b");
            if (pinDigits.Success)
            {
                result.IdNumber = ExtractedField<string>.From(pinDigits.Groups[1].Value.Trim());
            }
        }

        ExtractNameByLabels(result, text);
        PopulateFullName(result);
    }

    private static void ParseSssGsis(IdExtractionResult result, string text)
    {
        // SSS (10 digits: 00-0000000-0) or GSIS (11 digits)
        var sssMatch = Regex.Match(text, @"(?:SS|SSS|GSIS|CRN)?\s*(?:No|Number|\#)?[:\.\s]*\b(\d{2}[-\s]?\d{7}[-\s]?\d{1}|\d{11})\b", RegexOptions.IgnoreCase);
        if (sssMatch.Success)
        {
            result.IdNumber = ExtractedField<string>.From(sssMatch.Groups[1].Value.Replace(" ", "-").Trim());
        }

        ExtractNameByLabels(result, text);
        PopulateFullName(result);
    }

    private static void ParsePrcId(IdExtractionResult result, string text)
    {
        // Registration No: 7 digits
        var regMatch = Regex.Match(text, @"(?:Reg(?:istration)?\.?\s*No\.?|Certificate\s*No\.?)[:\.\s]*\b(\d{7})\b", RegexOptions.IgnoreCase);
        if (regMatch.Success)
        {
            result.IdNumber = ExtractedField<string>.From(regMatch.Groups[1].Value.Trim());
        }

        ExtractNameByLabels(result, text);
        PopulateFullName(result);
    }

    private static void ParseGenericFields(IdExtractionResult result, string text)
    {
        if (IdDocumentLabels.Read(text).TryGetValue("IdNumber", out var number) &&
            Regex.IsMatch(number, @"^[A-Z0-9][A-Z0-9\- /]{2,39}$", RegexOptions.IgnoreCase) &&
            Regex.IsMatch(number, @"\d") && !Regex.IsMatch(number, @"^\d{4}[-/]\d{2}[-/]\d{2}$"))
            result.IdNumber = ExtractedField<string>.From(number.Trim());
        ExtractNameByLabels(result, text);
        ExtractDobByLabels(result, text);
        ExtractSexByLabels(result, text);
        ExtractBloodTypeByLabels(result, text);
        ExtractAddressByLabels(result, text);
        PopulateFullName(result);
    }

    #endregion

    #region Common Field Extraction Helpers

    private static void ExtractNameByLabels(IdExtractionResult result, string text)
    {
        var fields = IdDocumentLabels.Read(text);
        result.LastName = ReadName("LastName");
        result.FirstName = ReadName("FirstName");
        result.MiddleName = ReadName("MiddleName");
        if (result.FirstName.Found || result.LastName.Found || result.MiddleName.Found)
            result.NameEvidence = IdNameEvidence.ExplicitComponents;
        if (!result.LastName.Found && !result.FirstName.Found && fields.TryGetValue("FullName", out var fullName))
        {
            SplitAndAssignFullName(result, CleanNameString(fullName, preserveComma: true));
            result.NameEvidence = IdNameEvidence.FullName;
        }

        ExtractedField<string> ReadName(string field)
        {
            if (!fields.TryGetValue(field, out var raw) ||
                (!IsName(raw) && !(field == "MiddleName" && Regex.IsMatch(raw, @"^\p{L}\.?$"))))
                return ExtractedField<string>.Empty();
            var name = CleanNameString(raw);
            return string.IsNullOrWhiteSpace(name) ? ExtractedField<string>.Empty() : ExtractedField<string>.From(name);
        }
    }

    internal static bool IsName(string raw) => raw.Length is >= 2 and <= 100 && raw.Count(char.IsLetter) >= 2 &&
        Regex.IsMatch(raw, @"^[\p{L}\p{M}]+(?:[\p{L}\p{M}\s.'’\-]*[\p{L}\p{M}.])?$") &&
        !Regex.IsMatch(raw, @"^(?:PHL|UNKNOWN|SINGLE|MALE|FEMALE|NAME|MIDDLE|LAST|FIRST)$", RegexOptions.IgnoreCase);

    private static void SplitAndAssignFullName(IdExtractionResult result, string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return;

        // A comma explicitly separates surname and given names. Do not invent a middle name.
        if (rawName.Contains(','))
        {
            var parts = rawName.Split(',', StringSplitOptions.TrimEntries);
            if (IsName(parts[0])) result.LastName = ExtractedField<string>.From(parts[0]);
            if (IsName(parts[1].Trim())) result.FirstName = ExtractedField<string>.From(parts[1].Trim());
            if (parts.Length == 3 && IsName(parts[2])) result.MiddleName = ExtractedField<string>.From(parts[2]);
        }
        else
        {
            if (!IsName(rawName)) return;
            // Standard "FIRST [MIDDLE] LAST"
            var parts = rawName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                result.FullName = ExtractedField<string>.From(parts[0]);
            }
            else if (parts.Length == 2)
            {
                result.FirstName = ExtractedField<string>.From(parts[0]);
                result.LastName = ExtractedField<string>.From(parts[1]);
            }
            else if (parts.Length >= 3)
            {
                result.FirstName = ExtractedField<string>.From(string.Join(" ", parts.Take(parts.Length - 1)));
                result.LastName = ExtractedField<string>.From(parts.Last());
            }
        }
    }

    private static void ExtractDobByLabels(IdExtractionResult result, string text)
    {
        if (IdDocumentLabels.Read(text).TryGetValue("DateOfBirth", out var rawDate) &&
            TryParseFlexibleDate(rawDate, out var date) && date.Year >= 1900 && date.Date <= DateTime.UtcNow.Date)
            result.DateOfBirth = ExtractedField<DateTime>.From(date);
    }

    private static void ExtractSexByLabels(IdExtractionResult result, string text)
    {
        if (IdDocumentLabels.Read(text).TryGetValue("Sex", out var value))
        {
            string raw = value.ToUpperInvariant();
            if (raw is "MALE" or "M" or "LALAKI") result.Sex = ExtractedField<string>.From("Male");
            else if (raw is "FEMALE" or "F" or "BABAE") result.Sex = ExtractedField<string>.From("Female");
        }
    }

    private static void ExtractBloodTypeByLabels(IdExtractionResult result, string text)
    {
        if (IdDocumentLabels.Read(text).TryGetValue("BloodType", out var value) &&
            Regex.IsMatch(value, @"^(?:AB|A|B|O)[+-]$", RegexOptions.IgnoreCase))
            result.BloodType = ExtractedField<string>.From(value.ToUpperInvariant());
    }

    private static void ExtractAddressByLabels(IdExtractionResult result, string text)
    {
        if (IdDocumentLabels.Read(text).TryGetValue("Address", out var address) && address.Length >= 5)
            result.Address = ExtractedField<string>.From(address);
    }

    private static void ExtractIdNumberByPattern(IdExtractionResult result, string text, string regexPattern)
    {
        var match = Regex.Match(text, regexPattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            result.IdNumber = ExtractedField<string>.From(match.Groups[1].Value.Trim());
        }
    }

    private static void PopulateFullName(IdExtractionResult result)
    {
        if (result.FirstName.Found || result.LastName.Found)
        {
            string first = result.FirstName.Value ?? "";
            string middle = result.MiddleName.Found && !string.IsNullOrWhiteSpace(result.MiddleName.Value) ? $" {result.MiddleName.Value} " : " ";
            string last = result.LastName.Value ?? "";
            result.FullName = ExtractedField<string>.From($"{first}{middle}{last}".Trim());
        }
    }

    private static string CleanNameString(string input, bool preserveComma = false)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        string pattern = preserveComma ? @"[^\p{L}\p{M}\s\-\.'’,]" : @"[^\p{L}\p{M}\s\-\.'’]";
        var cleaned = Regex.Replace(input, pattern, "").Trim();
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
    }

    private static string CleanMrzString(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var cleaned = input.Replace("<", " ").Trim();
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
    }

    private static bool TryParseMrzDate(string rawYYMMDD, bool isDob, out DateTime date)
    {
        date = default;
        if (rawYYMMDD.Length != 6) return false;

        if (int.TryParse(rawYYMMDD.Substring(0, 2), out int yy) &&
            int.TryParse(rawYYMMDD.Substring(2, 2), out int mm) &&
            int.TryParse(rawYYMMDD.Substring(4, 2), out int dd))
        {
            int currentYear2Digits = DateTime.UtcNow.Year % 100;
            int fullYear;
            if (isDob)
            {
                fullYear = (yy > currentYear2Digits) ? 1900 + yy : 2000 + yy;
            }
            else
            {
                fullYear = 2000 + yy;
            }

            try
            {
                date = new DateTime(fullYear, mm, dd, 0, 0, 0, DateTimeKind.Utc);
                return true;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    private static bool CheckMrzDigit(string value, char check)
    {
        if (!char.IsAsciiDigit(check)) return false;
        int[] weights = [7, 3, 1];
        var total = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var number = c is >= '0' and <= '9' ? c - '0' : c is >= 'A' and <= 'Z' ? c - 'A' + 10 : 0;
            total += number * weights[i % weights.Length];
        }
        return total % 10 == check - '0';
    }

    private static bool TryParseFlexibleDate(string raw, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // Reject damaged years and incomplete dates rather than letting culture parsing guess.
        if (!Regex.IsMatch(raw, @"^(?:\d{8}|[\p{L}\d\s/,.-]+)$")) return false;
        string clean = Regex.Replace(raw.Replace(",", " "), @"\s+", " ").Trim();
        clean = Regex.Replace(clean, @"\s*([/.-])\s*", "$1").TrimEnd('.', '-', ' ');
        string[] formats = new[]
        {
            "yyyy/MM/dd", "yyyy-MM-dd", "yyyy.MM.dd",
            "MM/dd/yyyy", "MM-dd-yyyy", "MM.dd.yyyy",
            "dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy",
            "M/d/yyyy", "d/M/yyyy",
            "dd MMM yyyy", "dd MMMM yyyy",
            "d MMM yyyy", "d MMMM yyyy",
            "MMM dd yyyy", "MMMM dd yyyy", "MMM d yyyy", "MMMM d yyyy",
            "yyyy MMM dd", "yyyy MMMM dd",
            "yyyyMMdd"
        };

        if (DateTime.TryParseExact(clean, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        return false;
    }

    #endregion
}
