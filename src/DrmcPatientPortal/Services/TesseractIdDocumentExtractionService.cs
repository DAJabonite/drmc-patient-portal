using System.Globalization;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Models;
using Tesseract;

namespace DrmcPatientPortal.Services;

public class TesseractIdDocumentExtractionService : IIdDocumentExtractionService
{
    private readonly ILogger<TesseractIdDocumentExtractionService> _logger;
    private readonly string _tessDataPath;

    public TesseractIdDocumentExtractionService(
        IWebHostEnvironment environment,
        ILogger<TesseractIdDocumentExtractionService> logger)
    {
        _logger = logger;
        
        // Locate tessdata directory
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string candidatePath = Path.Combine(baseDir, "tessdata");
        if (!Directory.Exists(candidatePath))
        {
            candidatePath = Path.Combine(environment.ContentRootPath, "tessdata");
        }
        _tessDataPath = candidatePath;
        _logger.LogInformation("TesseractIdDocumentExtractionService initialized with tessdata path: {Path}", _tessDataPath);
    }

    public async Task<IdExtractionResult> ExtractFromBytesAsync(string idType, byte[] frontImageBytes, byte[]? backImageBytes = null)
    {
        return await Task.Run(() =>
        {
            var result = new IdExtractionResult
            {
                IdType = idType,
                Success = false
            };

            if (frontImageBytes == null || frontImageBytes.Length == 0)
            {
                return result;
            }

            string frontText = string.Empty;
            float frontConfidence = 0.0f;
            string backText = string.Empty;
            float backConfidence = 0.0f;

            try
            {
                using var engine = new TesseractEngine(_tessDataPath, "eng", EngineMode.Default);
                
                // Process Front Image
                using (var pixFront = Pix.LoadFromMemory(frontImageBytes))
                using (var pageFront = engine.Process(pixFront))
                {
                    frontText = pageFront.GetText() ?? string.Empty;
                    frontConfidence = pageFront.GetMeanConfidence();
                }

                // Process Back Image if provided
                if (backImageBytes != null && backImageBytes.Length > 0)
                {
                    using (var pixBack = Pix.LoadFromMemory(backImageBytes))
                    using (var pageBack = engine.Process(pixBack))
                    {
                        backText = pageBack.GetText() ?? string.Empty;
                        backConfidence = pageBack.GetMeanConfidence();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tesseract OCR execution error for ID type: {IdType}", idType);
                return result;
            }

            result.RawTextFront = frontText;
            result.RawTextBack = backText;
            result.MeanConfidence = backImageBytes != null && backImageBytes.Length > 0
                ? (frontConfidence + backConfidence) / 2.0f
                : frontConfidence;

            result.Success = !string.IsNullOrWhiteSpace(frontText);

            // Layered structured field parsing per PhilippineIdType reference model
            ParseStructuredFields(result, idType, frontText, backText);

            return result;
        });
    }

    public async Task<IdExtractionResult> ExtractFromStreamsAsync(string idType, Stream frontImageStream, Stream? backImageStream = null)
    {
        using var msFront = new MemoryStream();
        await frontImageStream.CopyToAsync(msFront);
        byte[] frontBytes = msFront.ToArray();

        byte[]? backBytes = null;
        if (backImageStream != null)
        {
            using var msBack = new MemoryStream();
            await backImageStream.CopyToAsync(msBack);
            backBytes = msBack.ToArray();
        }

        return await ExtractFromBytesAsync(idType, frontBytes, backBytes);
    }

    private void ParseStructuredFields(IdExtractionResult result, string idType, string frontText, string backText)
    {
        var idConfig = PhilippineIdTypes.GetByName(idType);
        if (idConfig == null)
        {
            // Default loose extraction
            ParseGenericFields(result, frontText);
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
    }

    #region Specific ID Parsers

    private void ParsePassport(IdExtractionResult result, string text)
    {
        // 1. Attempt Machine Readable Zone (MRZ) 2-line ICAO 9303 Type P Parsing
        // Line 1: P<PHL<SURNAME<<GIVEN<NAMES<<<<<<<<<<<<
        // Line 2: PASSPORT_NO<PHLYYMMDD<SEX<YYMMDD<...
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => Regex.Replace(l.Trim().ToUpperInvariant(), @"\s+", ""))
                        .Where(l => l.Length >= 30)
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
                    if (!string.IsNullOrWhiteSpace(surname))
                    {
                        result.LastName = ExtractedField<string>.From(surname);
                    }
                }
                if (nameParts.Length >= 2)
                {
                    string givenPortion = CleanMrzString(nameParts[1]).Replace('<', ' ').Trim();
                    var givenWords = givenPortion.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (givenWords.Length > 1)
                    {
                        result.FirstName = ExtractedField<string>.From(givenWords[0]);
                        result.MiddleName = ExtractedField<string>.From(string.Join(" ", givenWords.Skip(1)));
                    }
                    else if (givenWords.Length == 1)
                    {
                        result.FirstName = ExtractedField<string>.From(givenWords[0]);
                    }
                }

                // Check subsequent line for MRZ Line 2
                if (i + 1 < lines.Count)
                {
                    string mrzLine2 = lines[i + 1];
                    if (mrzLine2.Length >= 28)
                    {
                        // Passport Number: First ~9 chars
                        string rawDocNum = mrzLine2.Substring(0, Math.Min(9, mrzLine2.Length)).Replace("<", "").Trim();
                        if (rawDocNum.Length >= 6)
                        {
                            result.IdNumber = ExtractedField<string>.From(rawDocNum);
                        }

                        // DOB: chars 13..19 (YYMMDD)
                        if (mrzLine2.Length >= 20)
                        {
                            string rawDob = mrzLine2.Substring(13, 6);
                            if (Regex.IsMatch(rawDob, @"^\d{6}$"))
                            {
                                if (TryParseMrzDate(rawDob, isDob: true, out DateTime dob))
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
        ExtractIdNumberByPattern(result, text, @"(?:Passport\s*(?:No|Number|\#)?[:\.\s]*)([A-Z0-9]{8,10})");
        ExtractDobByLabels(result, text);
        ExtractSexByLabels(result, text);
        PopulateFullName(result);
    }

    private void ParseDriversLicense(IdExtractionResult result, string text)
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

    private void ParsePhilSys(IdExtractionResult result, string frontText, string backText)
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

        // PhilSys Front Labels: "Last Name / Apelyido", "Given Names / Mga Pangalan", "Middle Name / Gitnang Pangalan"
        var lastNameMatch = Regex.Match(frontText, @"(?:Last\s*Name|Apelyido)[:\.\s/]+([A-Z\s\-]+?)(?=\n|Given|First|Middle|Date|Petsa|$)", RegexOptions.IgnoreCase);
        if (lastNameMatch.Success && !string.IsNullOrWhiteSpace(lastNameMatch.Groups[1].Value))
        {
            result.LastName = ExtractedField<string>.From(CleanNameString(lastNameMatch.Groups[1].Value));
        }

        var firstNameMatch = Regex.Match(frontText, @"(?:Given\s*Names?|First\s*Name|Mga\s*Pangalan)[:\.\s/]+([A-Z\s\-]+?)(?=\n|Middle|Gitnang|Last|Date|Petsa|$)", RegexOptions.IgnoreCase);
        if (firstNameMatch.Success && !string.IsNullOrWhiteSpace(firstNameMatch.Groups[1].Value))
        {
            result.FirstName = ExtractedField<string>.From(CleanNameString(firstNameMatch.Groups[1].Value));
        }

        var middleNameMatch = Regex.Match(frontText, @"(?:Middle\s*Name|Gitnang\s*Pangalan)[:\.\s/]+([A-Z\s\-]+?)(?=\n|Date|Petsa|Sex|Kasarian|$)", RegexOptions.IgnoreCase);
        if (middleNameMatch.Success && !string.IsNullOrWhiteSpace(middleNameMatch.Groups[1].Value))
        {
            result.MiddleName = ExtractedField<string>.From(CleanNameString(middleNameMatch.Groups[1].Value));
        }

        // Fallback names if not matched by specific PhilSys bilingual labels
        if (!result.LastName.Found && !result.FirstName.Found)
        {
            ExtractNameByLabels(result, frontText);
        }

        ExtractDobByLabels(result, frontText);
        ExtractAddressByLabels(result, frontText);

        // Back: Sex & Blood Type
        string combinedBack = string.IsNullOrWhiteSpace(backText) ? frontText : backText;
        ExtractSexByLabels(result, combinedBack);
        ExtractBloodTypeByLabels(result, combinedBack);

        PopulateFullName(result);
    }

    private void ParseUmid(IdExtractionResult result, string text)
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

    private void ParsePostalId(IdExtractionResult result, string text)
    {
        // Postal ID Reference Number
        var idMatch = Regex.Match(text, @"(?:Postal\s*ID\s*No|PRN|ID\s*No)[:\.\s]*([A-Z0-9\s\-]{6,20})", RegexOptions.IgnoreCase);
        if (idMatch.Success)
        {
            result.IdNumber = ExtractedField<string>.From(idMatch.Groups[1].Value.Trim());
        }

        ExtractNameByLabels(result, text);
        ExtractDobByLabels(result, text);
        ExtractAddressByLabels(result, text);
        PopulateFullName(result);
    }

    private void ParsePhilHealth(IdExtractionResult result, string text)
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

    private void ParseSssGsis(IdExtractionResult result, string text)
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

    private void ParsePrcId(IdExtractionResult result, string text)
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

    private void ParseGenericFields(IdExtractionResult result, string text)
    {
        ExtractNameByLabels(result, text);
        ExtractDobByLabels(result, text);
        ExtractSexByLabels(result, text);
        ExtractBloodTypeByLabels(result, text);
        ExtractAddressByLabels(result, text);
        PopulateFullName(result);
    }

    #endregion

    #region Common Field Extraction Helpers

    private void ExtractNameByLabels(IdExtractionResult result, string text)
    {
        // 1. Check for distinct Last / First / Middle Name labels
        var lastMatch = Regex.Match(text, @"(?:Last\s*Name|Surname|Apelyido)[:\.\s]+([A-Za-z\s\-]+?)(?=\n|First|Given|Middle|DOB|Date|$)", RegexOptions.IgnoreCase);
        if (lastMatch.Success)
        {
            result.LastName = ExtractedField<string>.From(CleanNameString(lastMatch.Groups[1].Value));
        }

        var firstMatch = Regex.Match(text, @"(?:First\s*Name|Given\s*Name(?:s)?|Pangalan)[:\.\s]+([A-Za-z\s\-]+?)(?=\n|Middle|Last|Surname|DOB|Date|$)", RegexOptions.IgnoreCase);
        if (firstMatch.Success)
        {
            result.FirstName = ExtractedField<string>.From(CleanNameString(firstMatch.Groups[1].Value));
        }

        var middleMatch = Regex.Match(text, @"(?:Middle\s*Name|Gitnang\s*Pangalan)[:\.\s]+([A-Za-z\s\-]+?)(?=\n|Last|Surname|First|DOB|Date|$)", RegexOptions.IgnoreCase);
        if (middleMatch.Success)
        {
            result.MiddleName = ExtractedField<string>.From(CleanNameString(middleMatch.Groups[1].Value));
        }

        // 2. If separate labels weren't found, search for unified "Name:" / "Full Name:"
        if (!result.LastName.Found && !result.FirstName.Found)
        {
            var nameMatch = Regex.Match(text, @"(?:Name|Full\s*Name|Member\s*Name|Cardholder)[:\.\s]+([A-Za-z\s\.,\-]+?)(?=\n|DOB|Date|Address|Sex|Blood|PIN|No|$)", RegexOptions.IgnoreCase);
            if (nameMatch.Success)
            {
                string rawName = CleanNameString(nameMatch.Groups[1].Value, preserveComma: true);
                SplitAndAssignFullName(result, rawName);
            }
        }
    }

    private void SplitAndAssignFullName(IdExtractionResult result, string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return;

        // Check if format is "LASTNAME, FIRSTNAME MIDDLENAME"
        if (rawName.Contains(','))
        {
            var parts = rawName.Split(new[] { ',' }, 2);
            result.LastName = ExtractedField<string>.From(parts[0].Trim());
            var firstAndMiddle = parts[1].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (firstAndMiddle.Length > 1)
            {
                result.FirstName = ExtractedField<string>.From(firstAndMiddle[0]);
                result.MiddleName = ExtractedField<string>.From(string.Join(" ", firstAndMiddle.Skip(1)));
            }
            else if (firstAndMiddle.Length == 1)
            {
                result.FirstName = ExtractedField<string>.From(firstAndMiddle[0]);
            }
        }
        else
        {
            // Standard "FIRST [MIDDLE] LAST"
            var parts = rawName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                result.FirstName = ExtractedField<string>.From(parts[0]);
            }
            else if (parts.Length == 2)
            {
                result.FirstName = ExtractedField<string>.From(parts[0]);
                result.LastName = ExtractedField<string>.From(parts[1]);
            }
            else if (parts.Length >= 3)
            {
                result.FirstName = ExtractedField<string>.From(parts[0]);
                result.MiddleName = ExtractedField<string>.From(string.Join(" ", parts.Skip(1).Take(parts.Length - 2)));
                result.LastName = ExtractedField<string>.From(parts.Last());
            }
        }
    }

    private void ExtractDobByLabels(IdExtractionResult result, string text)
    {
        var dobMatch = Regex.Match(text, @"(?:Date\s*of\s*Birth|Birth\s*Date|DOB|Birthday|Petsa\s*ng\s*Kapanganakan)[:\.\s]*([A-Za-z0-9\s,\/\-\.]+?)(?=\n|Sex|Gender|Address|Blood|Weight|Height|$)", RegexOptions.IgnoreCase);
        if (dobMatch.Success)
        {
            string rawDate = dobMatch.Groups[1].Value.Trim();
            if (TryParseFlexibleDate(rawDate, out DateTime dt))
            {
                result.DateOfBirth = ExtractedField<DateTime>.From(dt);
            }
        }
    }

    private void ExtractSexByLabels(IdExtractionResult result, string text)
    {
        var sexMatch = Regex.Match(text, @"(?:Sex|Gender|Kasarian)[:\.\s]*\b(MALE|FEMALE|M|F|LALAKI|BABAE)\b", RegexOptions.IgnoreCase);
        if (sexMatch.Success)
        {
            string raw = sexMatch.Groups[1].Value.ToUpperInvariant();
            if (raw is "MALE" or "M" or "LALAKI") result.Sex = ExtractedField<string>.From("Male");
            else if (raw is "FEMALE" or "F" or "BABAE") result.Sex = ExtractedField<string>.From("Female");
        }
    }

    private void ExtractBloodTypeByLabels(IdExtractionResult result, string text)
    {
        var bloodMatch = Regex.Match(text, @"(?:Blood\s*Type|Blood|Uri\s*ng\s*Dugo)[:\.\s]*(A\+|A\-|B\+|B\-|AB\+|AB\-|O\+|O\-|A|B|AB|O)(?=\s|$|\n|\r|,|\.)", RegexOptions.IgnoreCase);
        if (bloodMatch.Success)
        {
            result.BloodType = ExtractedField<string>.From(bloodMatch.Groups[1].Value.ToUpperInvariant().Trim());
        }
    }

    private void ExtractAddressByLabels(IdExtractionResult result, string text)
    {
        var addrMatch = Regex.Match(text, @"(?:Address|Residence|Tirahan)[:\.\s]+([A-Za-z0-9\s,\.\-#/]+?)(?=\n\n|Blood|Sex|Height|Weight|Date|Expiry|Signature|$)", RegexOptions.IgnoreCase);
        if (addrMatch.Success)
        {
            string raw = addrMatch.Groups[1].Value.Replace("\r", " ").Replace("\n", " ").Trim();
            raw = Regex.Replace(raw, @"\s+", " ");
            if (raw.Length >= 5)
            {
                result.Address = ExtractedField<string>.From(raw);
            }
        }
    }

    private void ExtractIdNumberByPattern(IdExtractionResult result, string text, string regexPattern)
    {
        var match = Regex.Match(text, regexPattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            result.IdNumber = ExtractedField<string>.From(match.Groups[1].Value.Trim());
        }
    }

    private void PopulateFullName(IdExtractionResult result)
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
        string pattern = preserveComma ? @"[^A-Za-z\s\-\.,]" : @"[^A-Za-z\s\-\.]";
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

    private static bool TryParseFlexibleDate(string raw, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string clean = Regex.Replace(raw, @"[^\w\s/\-\.]", "").Trim();
        string[] formats = new[]
        {
            "yyyy/MM/dd", "yyyy-MM-dd", "yyyy.MM.dd",
            "MM/dd/yyyy", "MM-dd-yyyy", "MM.dd.yyyy",
            "dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy",
            "dd MMM yyyy", "dd MMMM yyyy",
            "MMM dd, yyyy", "MMMM dd, yyyy",
            "yyyy MMM dd", "yyyy MMMM dd",
            "yyyyMMdd"
        };

        if (DateTime.TryParseExact(clean, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        return DateTime.TryParse(clean, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    #endregion
}
