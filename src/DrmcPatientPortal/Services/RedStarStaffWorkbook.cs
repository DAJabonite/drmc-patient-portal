using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Services;

// Only staff header definitions are bundled; report rows come from the authorized registry query.
internal static class RedStarStaffWorkbook
{
    private static readonly IReadOnlyDictionary<string, string[]> Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["LIVING"] =
        [
            "", "LEGEND", "NAME", "Testing Facility (Confirmed +)", "Age during Enrollment",
            "Current Age", "Address*", "HRN", "Patient Code", "Code Name",
            "Sex", "Category", "Perceived Sexuality", "Confirmatory Code", "UIC",
            "PHIC No.", "TB Status", "Tx Started", "Tx Completed", "Enrolment/Baseline Date",
            "ART Start Date @ RED STAR", "REFILL DATE", "Next Refill Date", "Last Contact No. Provided", "REGIMEN",
            "", "", "Latest CD4", "", "VIRAL LOAD",
            "", "TREATMENT STATUS", "OI", "OI Treatment Status", "DATE OF COMPLETION",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", ""
        ],
        ["REACTIVE WHEN TESTED, NONREACTI"] =
        [
            "No.", "LEGEND", "NAME", "Testing Facility (Confirmed +)", "Current Address",
            "HRN", "Patient Code", "Code Name", "Sex", "Perceived Sexuality",
            "Category", "SACCL Code", "UIC", "PHIC No.", "TB Status",
            "Tx Started", "Tx Completed", "Date Enrolled to RS-HACT", "ART Start Date @ RED STAR", "Last Visit @ RED STAR",
            "ARV Refill Date", "Last Contact No. Provided", "ARV Regimen", "", "",
            "Latest Viral Load (Date/Result)", "", "STATUS", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", ""
        ],
        ["TRANS OUT"] =
        [
            "", "LEG", "Patient Name", "Testing Facility (Confirmed +)", "Age during enrollment",
            "Current Address", "HRN", "Patient Code", "Code Name", "Sex",
            "Perceived Sexuality", "Category", "SACCL Code", "UIC", "PHIC No.",
            "TB Status", "Tx Started", "Tx Completed", "Date Enrolled @ RED STAR", "ART Start Date @ RED STAR",
            "Last Visit @ RED STAR", "ARV Refill Date", "Last Contact No. Provided", "ARV Regimen", "",
            "Date of Last Viral Load", "Result of Viral Load", "TRANSOUT DATE", "Name of Hub*", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", ""
        ],
        ["PrEP"] =
        [
            "", "", "Patient Name", "Testing Facility", "Current Address",
            "Age", "Patient Code", "Sex", "Perceived Sexuality", "Category",
            "UIC", "PHIC No.", "PrEP Start Date @ RED STAR", "Last Visit @ RED STAR", "PrEP NEXT Refill Date",
            "Last Contact No. Provided", "PrEP Regimen", "Date of last HIV Test", "Result HIV Test", "Date of last HBsAg",
            "Result of HBsAg", "Date of Last Creatinine", "Result of Creatinine", "Date of Last RPR/Syphilis Test", "Result of RPR/Syphilis",
            "STATUS"
        ],
        ["NB PROPHYLAXIS"] =
        [
            "No.", "LEG", "Patient Name", "Testing Facility", "Hospital No.",
            "Patient Code", "Code Name", "SEX", "Perceived Sexuality", "Category",
            "SACCL Code", "UIC", "PHIC No.", "First Consult @ RED STAR", "ART Start Date",
            "Last Visit at RED STAR", "REFILL DATE", "NEXT REFILL DATE", "ARV Regimen", "STATUS",
            "", "", "", "", "",
            "", "", ""
        ],
        ["PEP"] =
        [
            "No.", "LEG", "Patient Name", "Testing Facility", "Hospital No.",
            "Patient Code", "Code Name", "SEX", "Perceived Sexuality", "Category",
            "SACCL Code", "UIC", "PHIC No.", "First Consult @ RED STAR", "ART Start Date",
            "Last Visit at RED STAR", "ARV Regimen", "STATUS", "REMARKS", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            ""
        ],
        ["RHIVDA 2025"] =
        [
            "NAME"
        ],
        ["EXPIRED"] =
        [
            "", "LEG", "Patient Name", "Testing Facility Confirmed +", "Age during enrollment",
            "Last Address Resided", "HRN", "Patient Code", "Code Name", "SEX",
            "Perceived Sexuality", "Category", "SACCL Code", "UIC", "PHIC No.",
            "Lung Status", "Tx Started", "Tx Completed", "Date Enrolled to HACT", "ART Start Date @ RED STAR",
            "Last Visit @ RED STAR", "ARV REGIMEN", "", "Latest Viral Load Count (Date/Result)", "",
            "LOCATION of Expired", "DATE EXPIRED", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", ""
        ],
    };

    public static IReadOnlyList<string> ForSheet(string name) => Headers[RedStarRegistryLayout.Find(name)?.Name ?? name];

    private const string Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const int ColumnHeaderRow = 10;
    private static readonly double[] ReportWidths = [29, 24, 18, 23, 23, 27, 12];

    public static byte[] Report(RedStarMonthlySummary summary, DateTime generatedAt,
        IReadOnlyList<RedStarRegistryRow> rows, string appliedFilters)
    {
        var lastRow = ColumnHeaderRow + Math.Max(1, rows.Count);
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Part(string path, Action<XmlWriter> write)
            {
                using var target = zip.CreateEntry(path).Open();
                using var xml = XmlWriter.Create(target, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
                write(xml);
            }

            Part("[Content_Types].xml", xml =>
            {
                const string contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
                xml.WriteStartElement("Types", contentTypes);
                void Type(string element, string key, string value, string contentType)
                {
                    xml.WriteStartElement(element, contentTypes); xml.WriteAttributeString(key, value);
                    xml.WriteAttributeString("ContentType", contentType); xml.WriteEndElement();
                }
                Type("Default", "Extension", "rels", "application/vnd.openxmlformats-package.relationships+xml");
                Type("Default", "Extension", "xml", "application/xml");
                Type("Override", "PartName", "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                Type("Override", "PartName", "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                Type("Override", "PartName", "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
                xml.WriteEndElement();
            });
            Part("_rels/.rels", xml =>
            {
                xml.WriteStartElement("Relationships", PackageRelationships);
                Relationship(xml, "rId1", "officeDocument", "xl/workbook.xml");
                xml.WriteEndElement();
            });
            Part("xl/_rels/workbook.xml.rels", xml =>
            {
                xml.WriteStartElement("Relationships", PackageRelationships);
                Relationship(xml, "rId1", "worksheet", "worksheets/sheet1.xml");
                Relationship(xml, "rId2", "styles", "styles.xml");
                xml.WriteEndElement();
            });
            Part("xl/workbook.xml", xml =>
            {
                xml.WriteStartElement("workbook", Spreadsheet);
                xml.WriteStartElement("bookViews", Spreadsheet);
                xml.WriteStartElement("workbookView", Spreadsheet); xml.WriteEndElement(); xml.WriteEndElement();
                xml.WriteStartElement("sheets", Spreadsheet);
                xml.WriteStartElement("sheet", Spreadsheet);
                xml.WriteAttributeString("name", "Registry"); xml.WriteAttributeString("sheetId", "1");
                xml.WriteAttributeString("r", "id", OfficeRelationships, "rId1");
                xml.WriteEndElement(); xml.WriteEndElement();
                xml.WriteStartElement("definedNames", Spreadsheet);
                void DefinedName(string name, string value)
                {
                    xml.WriteStartElement("definedName", Spreadsheet);
                    xml.WriteAttributeString("name", name); xml.WriteAttributeString("localSheetId", "0");
                    xml.WriteString(value); xml.WriteEndElement();
                }
                DefinedName("_xlnm.Print_Titles", "'Registry'!$10:$10");
                DefinedName("_xlnm.Print_Area", "'Registry'!$A$1:$G$" + lastRow.ToString(CultureInfo.InvariantCulture));
                xml.WriteEndElement(); xml.WriteEndElement();
            });
            Part("xl/styles.xml", xml => xml.WriteRaw(ReportStyles));
            Part("xl/worksheets/sheet1.xml", xml => WriteReportSheet(xml, summary, generatedAt, rows, appliedFilters, lastRow));
        }
        return stream.ToArray();
    }

    private static void Relationship(XmlWriter xml, string id, string type, string target)
    {
        xml.WriteStartElement("Relationship", PackageRelationships);
        xml.WriteAttributeString("Id", id); xml.WriteAttributeString("Type", OfficeRelationships + "/" + type);
        xml.WriteAttributeString("Target", target); xml.WriteEndElement();
    }

    private static void WriteReportSheet(XmlWriter xml, RedStarMonthlySummary summary, DateTime generatedAt,
        IReadOnlyList<RedStarRegistryRow> rows, string appliedFilters, int lastRow)
    {
        void Element(string name, params (string Name, string Value)[] attributes)
        {
            xml.WriteStartElement(name, Spreadsheet);
            foreach (var attribute in attributes) xml.WriteAttributeString(attribute.Name, attribute.Value);
            xml.WriteEndElement();
        }
        void StartRow(int number, double height)
        {
            xml.WriteStartElement("row", Spreadsheet);
            xml.WriteAttributeString("r", number.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("ht", height.ToString(CultureInfo.InvariantCulture)); xml.WriteAttributeString("customHeight", "1");
        }
        void Text(string reference, string value, int style)
        {
            xml.WriteStartElement("c", Spreadsheet); xml.WriteAttributeString("r", reference);
            xml.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture)); xml.WriteAttributeString("t", "inlineStr");
            xml.WriteStartElement("is", Spreadsheet); xml.WriteStartElement("t", Spreadsheet);
            xml.WriteAttributeString("xml", "space", null, "preserve");
            // XmlWriter escapes patient content; formula-like text always remains literal text.
            xml.WriteString(value); xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement();
        }
        void Number(string reference, int value, int style)
        {
            xml.WriteStartElement("c", Spreadsheet); xml.WriteAttributeString("r", reference);
            xml.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
            xml.WriteElementString("v", Spreadsheet, value.ToString(CultureInfo.InvariantCulture)); xml.WriteEndElement();
        }
        void SummaryCount(int number, string label, int value)
        {
            StartRow(number, 22); Text("A" + number, label, 2); Number("C" + number, value, 3); xml.WriteEndElement();
        }

        xml.WriteStartElement("worksheet", Spreadsheet);
        xml.WriteStartElement("sheetPr", Spreadsheet); Element("pageSetUpPr", ("fitToPage", "1")); xml.WriteEndElement();
        Element("dimension", ("ref", "A1:G" + lastRow.ToString(CultureInfo.InvariantCulture)));
        xml.WriteStartElement("sheetViews", Spreadsheet);
        xml.WriteStartElement("sheetView", Spreadsheet); xml.WriteAttributeString("workbookViewId", "0"); xml.WriteAttributeString("showGridLines", "0");
        Element("pane", ("ySplit", "10"), ("topLeftCell", "A11"), ("activePane", "bottomLeft"), ("state", "frozen"));
        Element("selection", ("pane", "bottomLeft"), ("activeCell", "A11"), ("sqref", "A11"));
        xml.WriteEndElement(); xml.WriteEndElement();
        Element("sheetFormatPr", ("defaultRowHeight", "15"));
        xml.WriteStartElement("cols", Spreadsheet);
        for (var column = 0; column < ReportWidths.Length; column++)
        {
            var number = (column + 1).ToString(CultureInfo.InvariantCulture);
            Element("col", ("min", number), ("max", number), ("width", ReportWidths[column].ToString(CultureInfo.InvariantCulture)), ("customWidth", "1"));
        }
        xml.WriteEndElement();
        xml.WriteStartElement("sheetData", Spreadsheet);
        StartRow(1, 32); Text("A1", "Red Star Monthly Summary and Patient Registry", 1); xml.WriteEndElement();
        StartRow(2, 25);
        Text("A2", "Report Period", 2); Text("B2", summary.MonthStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture), 0);
        Text("D2", "Generated Date", 2); Text("E2", generatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture), 0);
        xml.WriteEndElement();
        SummaryCount(3, "Total Active", summary.ActiveCount);
        SummaryCount(4, "Total Living", summary.LivingCount);
        SummaryCount(5, "Total Expired for the Month", summary.ExpiredThisMonthCount);
        StartRow(6, WrappedHeight(appliedFilters, ReportWidths.Skip(1).Sum()));
        Text("A6", "Applied filters", 2); Text("B6", appliedFilters, 0); xml.WriteEndElement();
        StartRow(7, 22); Text("A7", "Matching rows", 2); Number("B7", rows.Count, 3); xml.WriteEndElement();
        StartRow(8, 30);
        Text("A8", "Registry-wide totals. Active and Living (active ART) are current at generation; only Expired uses the report period.", 8);
        xml.WriteEndElement();
        StartRow(9, 8); xml.WriteEndElement();
        StartRow(ColumnHeaderRow, 28);
        var headers = new[] { "Patient code", "Code name", "Program", "Status", "Next refill", "Follow-up", "Requests" };
        for (var column = 0; column < headers.Length; column++) Text(((char)('A' + column)).ToString() + ColumnHeaderRow, headers[column], 4);
        xml.WriteEndElement();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var number = ColumnHeaderRow + index + 1;
            var status = row.Status == RedStarStatus.Deceased ? "Expired" +
                (row.StatusDate.HasValue ? "\n" + row.StatusDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "") : AdminChips.Humanize(row.Status);
            var nextRefill = row.NextRefill.HasValue ? row.NextRefill.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) +
                (row.NextRefillVerified != true ? "\nNeeds verification" : "") : "Not scheduled";
            var values = new[] { row.PatientCode, row.CodeName, row.Program.ToString(), status, nextRefill,
                row.FollowUpReview ? "Follow-up / LTFU review" : "—" };
            StartRow(number, values.Select((value, column) => WrappedHeight(value, ReportWidths[column])).Max());
            for (var column = 0; column < values.Length; column++) Text(((char)('A' + column)).ToString() + number, values[column], column == 4 ? 6 : 5);
            Number("G" + number, row.PendingRequests, 7); xml.WriteEndElement();
        }
        if (rows.Count == 0)
        {
            StartRow(11, 28); Text("A11", "No records match these filters.", 8); xml.WriteEndElement();
        }
        xml.WriteEndElement();
        if (rows.Count != 0) Element("autoFilter", ("ref", "A10:G" + lastRow.ToString(CultureInfo.InvariantCulture)));
        var merges = new List<string> { "A1:G1", "B2:C2", "E2:G2", "A3:B3", "A4:B4", "A5:B5", "B6:G6", "A8:G8" };
        if (rows.Count == 0) merges.Add("A11:G11");
        xml.WriteStartElement("mergeCells", Spreadsheet); xml.WriteAttributeString("count", merges.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var merge in merges) Element("mergeCell", ("ref", merge));
        xml.WriteEndElement();
        Element("printOptions", ("horizontalCentered", "1"));
        Element("pageMargins", ("left", "0.3"), ("right", "0.3"), ("top", "0.4"), ("bottom", "0.4"), ("header", "0.2"), ("footer", "0.2"));
        Element("pageSetup", ("paperSize", "9"), ("orientation", "landscape"), ("fitToWidth", "1"), ("fitToHeight", "0"));
        xml.WriteStartElement("headerFooter", Spreadsheet);
        xml.WriteElementString("oddFooter", Spreadsheet, "&CPage &P of &N");
        xml.WriteEndElement(); xml.WriteEndElement();
    }

    private static double WrappedHeight(string value, double columnWidth)
    {
        // Estimate Calibri's visual widths conservatively without requiring Excel or font rendering.
        // Runes keep supplementary glyphs intact; word boundaries and padding allow safe wrapped rows.
        var capacity = Math.Max(1, columnWidth - 2);
        var totalLines = 0;
        foreach (var line in value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var lines = 1;
            double occupied = 0, word = 0, spaces = 0;
            void PlaceWord()
            {
                if (word == 0) return;
                if (occupied > 0 && occupied + spaces + word > capacity) { lines++; occupied = 0; }
                else if (occupied > 0) occupied += spaces;
                var wrappedLines = Math.Max(1, (int)Math.Ceiling(word / capacity));
                if (wrappedLines > 1) { lines += wrappedLines - 1; occupied = word - (wrappedLines - 1) * capacity; }
                else occupied += word;
                word = 0; spaces = 0;
            }
            foreach (var rune in line.EnumerateRunes())
            {
                if (Rune.IsWhiteSpace(rune)) { PlaceWord(); spaces += rune.Value == '\t' ? 4 : 1; }
                else word += GlyphWidth(rune);
            }
            PlaceWord();
            totalLines += lines;
        }
        return Math.Max(28, totalLines * 16 + 8);
    }

    private static double GlyphWidth(Rune rune)
    {
        if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) return 0;
        var scalar = rune.Value;
        if (scalar is >= 0x1100 and <= 0x115F or >= 0x2329 and <= 0x232A or >= 0x2600 and <= 0x27BF or
            >= 0x2E80 and <= 0xA4CF or >= 0xAC00 and <= 0xD7A3 or >= 0xF900 and <= 0xFAFF or
            >= 0xFE10 and <= 0xFE19 or >= 0xFE30 and <= 0xFE6F or >= 0xFF01 and <= 0xFF60 or
            >= 0xFFE0 and <= 0xFFE6 or >= 0x1F000 and <= 0x1FAFF or >= 0x20000 and <= 0x3FFFD or
            'W' or 'M' or '@' or '%' or '&') return 2;
        if (scalar is 'w' or 'm') return 1.6;
        return Rune.IsUpper(rune) ? 1.4 : 1;
    }

    // Fixed styles contain no patient data; indexes are shared by summary and registry cells above.
    private const string ReportStyles = """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="4">
            <font><sz val="11"/><color rgb="FF1F2937"/><name val="Calibri"/></font>
            <font><b/><sz val="18"/><color rgb="FF8B1E2D"/><name val="Calibri"/></font>
            <font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font>
            <font><b/><sz val="11"/><color rgb="FF1F2937"/><name val="Calibri"/></font>
          </fonts>
          <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF8B1E2D"/><bgColor indexed="64"/></patternFill></fill></fills>
          <borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style="thin"><color rgb="FFD1D5DB"/></left><right style="thin"><color rgb="FFD1D5DB"/></right><top style="thin"><color rgb="FFD1D5DB"/></top><bottom style="thin"><color rgb="FFD1D5DB"/></bottom><diagonal/></border></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="9">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="3" fillId="0" borderId="0" xfId="0" applyFont="1" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="3" fillId="0" borderId="0" xfId="0" applyFont="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="top" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="top" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="top" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="left" vertical="center" wrapText="1"/></xf>
          </cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """;
}
