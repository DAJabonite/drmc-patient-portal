using System.Diagnostics;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Models;
using Tesseract;

namespace DrmcPatientPortal.Services;

/// <summary>Runs only inside the killable OCR worker. All derived images remain in memory.</summary>
internal sealed class IdImageRecognition(TesseractEngine engine, string idType, Stopwatch elapsed)
{
    internal sealed record Line(string Text, Rect Bounds, float Confidence);
    internal sealed record Reading(string Text, float Confidence, IReadOnlyList<Line> Lines, IReadOnlyList<Line> Words);
    internal sealed record Result(string OriginalText, float Confidence, IdExtractionResult Fields);
    private readonly List<(ExtractedField<string> Field, int Scale)> addressCandidates = [];

    // Leave time for the current native call, parsing and IPC before the parent's ten-second kill.
    private bool CanRetry => elapsed.Elapsed < TimeSpan.FromSeconds(7);

    public Result Read(Pix original, bool back)
    {
        addressCandidates.Clear();
        var result = new IdExtractionResult { IdType = idType };
        // These are valid images, but cannot contain a legible ID. Permit manual entry.
        if (!string.IsNullOrWhiteSpace(idType) && (original.Width < 16 || original.Height < 16))
            return new("", 0, result);
        var first = ReadPage(original, PageSegMode.Auto);
        Add(first, original.Width);
        // Runtime startup probes verify decoding and recognition, not ID layout recovery.
        if (!CanRetry || string.IsNullOrWhiteSpace(idType)) return Finish();

        // Small security-print labels need more pixels; bound every working image to 2048 on its long side.
        var longSide = Math.Max(original.Width, original.Height);
        var scale = Math.Min(3f, (longSide < 1600 ? 1600f : 2048f) / longSide);
        using var resized = original.Scale(scale, scale);
        using var gray = resized.ConvertTo8(0);
        var readings = new List<Reading>();
        if (NeedsRetry(result, back) && CanRetry)
        {
            var reading = ReadPage(gray, PageSegMode.Auto);
            readings.Add(reading);
            Add(reading, gray.Width);
        }
        if (NeedsRetry(result, back) && CanRetry)
        {
            var reading = ReadPage(gray, PageSegMode.SparseText);
            readings.Add(reading);
            Add(reading, gray.Width);
        }
        if (CanRetry && readings.Count > 0 && gray.Width > 32 && gray.Height > 32)
        {
            using var threshold = gray.BinarizeOtsuAdaptiveThreshold(200, 200, 0, 0, .1f);
            // Read known card layouts before spending the shared front/back budget on generic retries.
            RefineKnownLayout(original, gray, threshold, readings, result, back);
            if (NeedsRetry(result, back) && CanRetry) Add(ReadPage(threshold, PageSegMode.Auto), gray.Width);
            if (NeedsRetry(result, back) && CanRetry)
            {
                // Large dark QR modules can make global thresholds erase thin, low-contrast labels.
                using var localThreshold = gray.BinarizeSauvola(16, .15f, true);
                var local = ReadPage(localThreshold, PageSegMode.SparseText);
                readings.Add(local);
                Add(local, gray.Width);
            }
            if (NeedsRetry(result, back) && CanRetry)
            {
                // Leptonica keeps the original when skew evidence is weak. This is a
                // single bounded retry; its coordinates are never mixed with the source.
                using var deskewed = gray.Deskew(out var skew);
                if (skew.Confidence >= 3 && Math.Abs(skew.Angle) is >= .2f and <= 7.5f && CanRetry)
                    Add(ReadPage(deskewed, PageSegMode.Auto), deskewed.Width);
            }
            if (NeedsRetry(result, back))
            {
                var labels = readings.SelectMany(r => r.Lines)
                    .Where(l => IdDocumentLabels.Find(l.Text).Any(a => a.Field != "Other"))
                    .GroupBy(l => l.Bounds.X1 / Math.Max(1, gray.Width / 10)).Where(g => g.Count() >= 2).Take(3).ToArray();
                foreach (var group in labels)
                {
                    if (!CanRetry) break;
                    var left = group.Min(l => l.Bounds.X1) - 16;
                    var right = left + Math.Max(gray.Width * .45, group.Max(l => l.Bounds.Width) * 2.5);
                    var area = Clamp(gray, left, 0, (int)right, gray.Height);
                    var column = ReadPage(gray, PageSegMode.SingleBlock, area);
                    readings.Add(column);
                    Add(column, gray.Width);
                    if (CanRetry)
                    {
                        column = ReadPage(threshold, PageSegMode.SingleBlock, area);
                        readings.Add(column);
                        Add(column, gray.Width);
                    }
                }
            }
            if (NeedsRetry(result, back))
            {
                RefineColumns(gray, threshold, readings, result, back);
                RefineLabels(gray, threshold, readings.SelectMany(r => r.Lines).ToArray(), result, back);
            }
        }
        return Finish();

        void Add(Reading reading, int scale)
        {
            var parsed = Parse(reading.Text, back);
            IdExtractionCandidates.Merge(result, parsed, reading.Confidence);
            if (parsed.Address.Found) addressCandidates.Add((parsed.Address with { Confidence = reading.Confidence }, scale));
        }
        Result Finish()
        {
            result.Address = ResolveAddress(result.Address, addressCandidates);
            return new(first.Text, result.FoundFieldsSummary.Count == 0 ? first.Confidence : MeanConfidence(result), result);
        }
    }

    internal static ExtractedField<string> ResolveAddress(ExtractedField<string> current,
        IEnumerable<(ExtractedField<string> Field, int Scale)> candidates)
    {
        // Count independent sampling scales, not repeated passes over the same pixel grid.
        var postcodes = candidates.Where(c => c.Field.Found && c.Field.Confidence >= .7f)
            .Select(c => (Candidate: c, Postcode: Regex.Match(c.Field.Value ?? "", @"\b\d{4}$").Value))
            .Where(c => c.Postcode.Length > 0).GroupBy(c => c.Postcode)
            .Select(g => new { Votes = g.Select(c => c.Candidate.Scale).Distinct().Count(),
                Best = g.Select(c => c.Candidate.Field).MaxBy(f => f.Confidence)! })
            .OrderByDescending(g => g.Votes).ToArray();
        return postcodes.Length > 0 && postcodes[0].Votes >= 2 &&
            (postcodes.Length == 1 || postcodes[0].Votes > postcodes[1].Votes) ? postcodes[0].Best : current;
    }

    private static float MeanConfidence(IdExtractionResult result)
    {
        var scores = new List<float>();
        Add(result.FirstName); Add(result.MiddleName); Add(result.LastName); Add(result.IdNumber);
        Add(result.DateOfBirth); Add(result.Address); Add(result.Sex); Add(result.BloodType);
        return scores.Count == 0 ? 0 : scores.Average();
        void Add<T>(ExtractedField<T> field) { if (field.Found) scores.Add(field.Confidence); }
    }

    private bool NeedsRetry(IdExtractionResult fields, bool back)
    {
        if (back) return !fields.Sex.Found || fields.Sex.Confidence < .85f;
        var type = PhilippineIdTypes.GetByName(idType);
        return !fields.FirstName.Found || !fields.LastName.Found || !fields.IdNumber.Found ||
            fields.FirstName.Confidence < .85f || fields.LastName.Confidence < .85f ||
            (type?.SupportsDob == true && !fields.DateOfBirth.Found) ||
            (type?.SupportsAddress == true && !fields.Address.Found) ||
            (type?.SupportsSex == true && type.NeedsBackPhoto == false && !fields.Sex.Found);
    }

    private IdExtractionResult Parse(string text, bool back)
    {
        var result = new IdExtractionResult { IdType = idType };
        TesseractIdDocumentExtractionService.ParseStructuredFields(result, idType, back ? "" : text, back ? text : "");
        return result;
    }

    private Reading ReadPage(Pix image, PageSegMode mode, Rect? region = null)
    {
        using var page = region is { } rectangle ? engine.Process(image, rectangle, mode) : engine.Process(image, mode);
        var lines = new List<Line>();
        using var iterator = page.GetIterator();
        iterator.Begin();
        do
        {
            var text = iterator.GetText(PageIteratorLevel.TextLine)?.Trim();
            if (!string.IsNullOrEmpty(text) && iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out var bounds))
                lines.Add(new(text, bounds, iterator.GetConfidence(PageIteratorLevel.TextLine) / 100));
        } while (iterator.Next(PageIteratorLevel.TextLine));
        var words = new List<Line>();
        iterator.Begin();
        do
        {
            var text = iterator.GetText(PageIteratorLevel.Word)?.Trim();
            if (!string.IsNullOrEmpty(text) && iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds))
                words.Add(new(text, bounds, iterator.GetConfidence(PageIteratorLevel.Word) / 100));
        } while (iterator.Next(PageIteratorLevel.Word));
        return new(page.GetText() ?? "", page.GetMeanConfidence(), lines, words);
    }

    private void RefineLabels(Pix gray, Pix threshold, IReadOnlyList<Line> lines, IdExtractionResult fields, bool back)
    {
        var seen = new HashSet<(string Field, int X, int Y)>();
        foreach (var label in lines.OrderByDescending(l => l.Confidence))
        {
            if (!CanRetry) break;
            var labels = IdDocumentLabels.Find(label.Text).Where(l => l.Field != "Other").ToArray();
            // Adjacent columns are associated by their own x coordinates in the sparse-text pass.
            if (labels.Select(l => l.Field).Distinct().Count() != 1) continue;
            var field = labels[0].Field;
            if (!seen.Add((field, label.Bounds.X1 / 24, label.Bounds.Y1 / 24))) continue;
            var right = Math.Min(gray.Width, label.Bounds.X2 + Math.Max(label.Bounds.Width / 2, 30));
            var nextLabelTop = lines.Where(l => l.Bounds.Y1 > label.Bounds.Y2 &&
                    Math.Abs(l.Bounds.X1 - label.Bounds.X1) < Math.Max(label.Bounds.Width / 2, 40) &&
                    IdDocumentLabels.Find(l.Text).Count > 0).Select(l => l.Bounds.Y1).DefaultIfEmpty(gray.Height).Min();
            var below = lines.Where(l => l.Bounds.Y1 >= label.Bounds.Y2 - 2 &&
                    l.Bounds.Y2 <= nextLabelTop + 2 &&
                    l.Bounds.Y1 - label.Bounds.Y2 < Math.Max(label.Bounds.Height * 5, 90) &&
                    Math.Abs(l.Bounds.X1 - label.Bounds.X1) < Math.Max(label.Bounds.Width / 2, 40) &&
                    l.Bounds.X1 < right && IdDocumentLabels.Find(l.Text).Count == 0 &&
                    (field is "FirstName" or "MiddleName" or "LastName" or "FullName"
                        ? Regex.IsMatch(l.Text, @"\p{L}{3}") : Regex.IsMatch(l.Text, @"[\p{L}\d]{2}")))
                .OrderBy(l => l.Bounds.Y1).ThenByDescending(l => l.Confidence).ToArray();
            if (below.Length == 0) continue;
            var value = below[0];
            var region = Clamp(gray, Math.Min(label.Bounds.X1, value.Bounds.X1) - 8, value.Bounds.Y1 - 5,
                Math.Max(label.Bounds.X2, value.Bounds.X2) + 8, value.Bounds.Y2 + 5);
            if (field == "Address")
            {
                var addressLines = below.Where(l => l.Bounds.Y1 < value.Bounds.Y2 + Math.Max(value.Bounds.Height * 3, 50)).ToArray();
                region = Clamp(gray, addressLines.Min(l => l.Bounds.X1) - 5, value.Bounds.Y1 - 4,
                    addressLines.Max(l => l.Bounds.X2) + 5, addressLines.Max(l => l.Bounds.Y2) + 4);
            }
            ReadField(gray, region, field, fields, back);
            if (CanRetry) ReadField(threshold, region, field, fields, back);
        }
    }

    private void RefineColumns(Pix gray, Pix threshold, IEnumerable<Reading> readings, IdExtractionResult fields, bool back)
    {
        foreach (var reading in readings)
        foreach (var line in reading.Lines)
        {
            if (!CanRetry) return;
            var words = reading.Words.Where(w => w.Bounds.Y1 >= line.Bounds.Y1 - 2 &&
                w.Bounds.Y2 <= line.Bounds.Y2 + 2 && w.Bounds.X1 >= line.Bounds.X1 - 2 &&
                w.Bounds.X2 <= line.Bounds.X2 + 2).OrderBy(w => w.Bounds.X1).ToArray();
            var text = string.Join(" ", words.Select(w => w.Text));
            var labels = IdDocumentLabels.Find(text);
            if (labels.Select(l => l.Field).Distinct().Count() < 2) continue;
            // Only rows of headers qualify. Inline "Sex: M Date of Birth: ..." already has values.
            var residue = text;
            foreach (var label in labels.Reverse()) residue = residue.Remove(label.Start, label.End - label.Start);
            if (Regex.IsMatch(residue, @"[\p{L}\d]")) continue;
            // The LTO heading describes one name row, not three aligned value columns.
            if (idType == PhilippineIdTypes.DriversLicense && IdDocumentLabels.IsLtoNameHeader(text)) continue;
            var offsets = new List<(Line Word, int Start, int End)>();
            var offset = 0;
            foreach (var word in words)
            {
                offsets.Add((word, offset, offset + word.Text.Length));
                offset += word.Text.Length + 1;
            }
            for (var i = 0; i < labels.Count && CanRetry; i++)
            {
                var label = labels[i];
                if (label.Field == "Other") continue;
                var labelWords = offsets.Where(w => w.End > label.Start && w.Start < label.End).ToArray();
                if (labelWords.Length == 0) continue;
                var left = labelWords.Min(w => w.Word.Bounds.X1) - 8;
                var right = i + 1 < labels.Count
                    ? offsets.First(w => w.End > labels[i + 1].Start).Word.Bounds.X1 - 8 : line.Bounds.X2 + 30;
                var below = reading.Words.Where(w => w.Bounds.Y1 >= line.Bounds.Y2 &&
                    w.Bounds.Y1 < line.Bounds.Y2 + Math.Max(90, line.Bounds.Height * 5) &&
                    w.Bounds.X1 >= left && w.Bounds.X1 < right).OrderBy(w => w.Bounds.Y1).ToArray();
                if (below.Length == 0) continue;
                var row = below.Where(w => Math.Abs(w.Bounds.Y1 - below[0].Bounds.Y1) < Math.Max(8, below[0].Bounds.Height / 2)).ToArray();
                var area = Clamp(gray, row.Min(w => w.Bounds.X1) - 5, row.Min(w => w.Bounds.Y1) - 4,
                    row.Max(w => w.Bounds.X2) + 5, row.Max(w => w.Bounds.Y2) + 4);
                ReadField(gray, area, label.Field, fields, back);
                if (CanRetry) ReadField(threshold, area, label.Field, fields, back);
            }
        }
    }

    private void RefineKnownLayout(Pix original, Pix gray, Pix threshold, IReadOnlyList<Reading> readings, IdExtractionResult fields, bool back)
    {
        var text = string.Join("\n", readings.Select(r => r.Text));
        if (idType == PhilippineIdTypes.DriversLicense && !back)
            RefineLtoNameRow(gray, threshold, readings, fields);
        if (idType == PhilippineIdTypes.Passport && CanRetry)
        {
            var mrz = ReadPage(threshold, PageSegMode.SingleBlock, Relative(gray, 0, .68, 1, .32));
            IdExtractionCandidates.Merge(fields, Parse(mrz.Text, back), mrz.Confidence);
        }
        if (idType != PhilippineIdTypes.PhilSys) return;
        if (back)
        {
            if (!CanRetry || !Regex.IsMatch(text, @"(?:Kasari|Sex|Blood)", RegexOptions.IgnoreCase)) return;
            var column = ReadPage(gray, PageSegMode.SingleBlock, Relative(gray, .025, .1, .405, .83));
            IdExtractionCandidates.Merge(fields, Parse(column.Text, true), column.Confidence);
            return;
        }

        // The classic PhilSys layout has an unlabelled PCN. Apply its template only when the
        // card is tightly framed and at least two detected labels agree with that layout.
        var ratio = (double)gray.Width / gray.Height;
        var anchors = readings.SelectMany(r => r.Lines).Where(l => l.Bounds.X1 / (double)gray.Width is > .25 and < .36)
            .SelectMany(l => IdDocumentLabels.Find(l.Text).Select(a => (a.Field, Y: l.Bounds.Y1 / (double)gray.Height)))
            .Where(a => a.Field switch
            {
                "FirstName" => a.Y is > .40 and < .50,
                "MiddleName" => a.Y is > .55 and < .65,
                "DateOfBirth" => a.Y is > .70 and < .78,
                _ => false
            }).Select(a => a.Field).Distinct().Count();
        if (ratio is < 1.5 or > 1.7 || anchors < 2 ||
            !Regex.IsMatch(text, @"PILIPINAS|Identification", RegexOptions.IgnoreCase)) return;

        foreach (var area in new (string Field, double X, double Y, double W, double H)[]
        {
            ("IdNumber", .027, .265, .27, .057),
            ("LastName", .286, .34, .38, .083),
            ("FirstName", .286, .47, .38, .094),
            ("MiddleName", .286, .615, .38, .081),
            ("DateOfBirth", .286, .762, .35, .075),
            ("Address", .02, .854, .94, .13)
        })
        {
            if (!CanRetry) break;
            var region = Relative(gray, area.X, area.Y, area.W, area.H);
            ReadField(gray, region, area.Field, fields, false);
            if (CanRetry) ReadField(threshold, region, area.Field, fields, false);
        }
        if (CanRetry)
        {
            // A second sampling scale avoids turning a faint loop (0/6/8) into a confident digit.
            var scale = Math.Min(2f, 2048f / Math.Max(original.Width, original.Height));
            using var alternate = original.Scale(scale, scale);
            using var alternateGray = alternate.ConvertTo8(0);
            using var alternateThreshold = alternateGray.BinarizeOtsuAdaptiveThreshold(200, 200, 0, 0, .1f);
            if (!fields.DateOfBirth.Found)
                ReadField(alternateThreshold, Relative(alternateGray, .286, .762, .35, .075), "DateOfBirth", fields, false);
            if (CanRetry)
                ReadField(alternateThreshold, Relative(alternateGray, .02, .854, .94, .13), "Address", fields, false);
        }
        if (CanRetry && original.Width < 1024)
        {
            var scale = Math.Min(4f, 2048f / Math.Max(original.Width, original.Height));
            using var alternate = original.Scale(scale, scale);
            using var alternateGray = alternate.ConvertTo8(0);
            using var alternateThreshold = alternateGray.BinarizeOtsuAdaptiveThreshold(200, 200, 0, 0, .1f);
            var area = Relative(alternateGray, .02, .854, .94, .13);
            ReadField(alternateGray, area, "Address", fields, false);
            if (CanRetry) ReadField(alternateThreshold, area, "Address", fields, false);
        }
    }

    private void RefineLtoNameRow(Pix gray, Pix threshold, IReadOnlyList<Reading> readings, IdExtractionResult fields)
    {
        foreach (var reading in readings)
        foreach (var header in reading.Lines.Where(line => IdDocumentLabels.IsLtoNameHeader(line.Text)))
        {
            if (!CanRetry) return;
            var value = reading.Lines.Where(line => line.Bounds.Y1 >= header.Bounds.Y2 - 2 &&
                line.Bounds.Y1 < header.Bounds.Y2 + Math.Max(90, header.Bounds.Height * 5) &&
                line.Bounds.X1 >= header.Bounds.X1 - 32 && line.Bounds.X1 < header.Bounds.X2 &&
                IdDocumentLabels.Find(line.Text).Count == 0 && Regex.IsMatch(line.Text, @"\p{L}{2}"))
                .OrderBy(line => line.Bounds.Y1).FirstOrDefault();
            if (value is null) continue;
            var area = Clamp(gray, Math.Min(header.Bounds.X1, value.Bounds.X1) - 12,
                value.Bounds.Y1 - 8, gray.Width, value.Bounds.Y2 + 8);
            ReadField(gray, area, "LtoName", fields, false);
            if (CanRetry) ReadField(threshold, area, "LtoName", fields, false);
        }
    }

    private void ReadField(Pix image, Rect region, string field, IdExtractionResult fields, bool back)
    {
        var label = field switch
        {
            "IdNumber" => idType switch
            {
                PhilippineIdTypes.Passport => "Passport No.", PhilippineIdTypes.DriversLicense => "License No.",
                PhilippineIdTypes.Umid => "CRN", PhilippineIdTypes.PhilHealth => "PIN",
                PhilippineIdTypes.SssGsis => "SSS No.", PhilippineIdTypes.PrcId => "Registration No.", _ => "ID Number"
            },
            "DateOfBirth" => "Date of Birth", "FirstName" => "First Name",
            "LtoName" => "Last Name, First Name, Middle Name",
            "LastName" => "Last Name", "MiddleName" => "Middle Name", "FullName" => "Full Name",
            "BloodType" => "Blood Type", _ => field
        };
        var reading = ReadPage(image, field == "Address" ? PageSegMode.SingleBlock : PageSegMode.SingleLine, region);
        var text = reading.Text.Trim();
        if (field == "Address")
        {
            // Remove edge artwork, not spelling or numbers inside the address.
            text = string.Join(" ", reading.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanAddressLine)
                .Where(l => l.Length > 0));
        }
        var parsed = Parse(label + ": " + text, back);
        // Field-focused reads have stronger spatial evidence than a whole-page reading.
        var addressConfidence = reading.Confidence;
        if (field == "Address")
        {
            // Whole-line averages hide an uncertain postcode amongst many clear words.
            var postalWords = reading.Words.Where(w => Regex.IsMatch(w.Text, @"^\d{4}$")).ToArray();
            if (postalWords.Length > 0) addressConfidence = Math.Min(addressConfidence, postalWords.Min(w => w.Confidence));
        }
        IdExtractionCandidates.Merge(fields, parsed, Math.Min(1, reading.Confidence + .04f),
            field == "Address" ? Math.Min(1, addressConfidence + .04f) : null);
        if (parsed.Address.Found)
            addressCandidates.Add((parsed.Address with { Confidence = Math.Min(1, reading.Confidence + .04f) }, image.Width));
    }

    private static string CleanAddressLine(string line)
    {
        line = Regex.Replace(line, @"[^\p{L}\d.,#'’/\-\s].*$", "").Trim();
        line = Regex.Replace(line, @"(\bPHILIPPINES)\s*,?\s*[^\d]*$", "$1", RegexOptions.IgnoreCase);
        var postal = Regex.Match(line, @"^(\d{4})(?:\s*[^\p{L}\d\s]+\s*[\p{L}\s]{0,3})?$");
        return postal.Success ? postal.Groups[1].Value : line;
    }

    private static Rect Relative(Pix image, double x, double y, double width, double height) =>
        Clamp(image, (int)(image.Width * x), (int)(image.Height * y),
            (int)(image.Width * (x + width)), (int)(image.Height * (y + height)));

    private static Rect Clamp(Pix image, int left, int top, int right, int bottom)
    {
        left = Math.Clamp(left, 0, image.Width - 1); top = Math.Clamp(top, 0, image.Height - 1);
        return new(left, top, Math.Clamp(right, left + 1, image.Width) - left, Math.Clamp(bottom, top + 1, image.Height) - top);
    }
}
