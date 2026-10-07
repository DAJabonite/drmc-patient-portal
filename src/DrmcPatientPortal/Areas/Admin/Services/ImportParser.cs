using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using DrmcPatientPortal.Areas.Admin.Models;
using ExcelDataReader;
using Microsoft.VisualBasic.FileIO;

namespace DrmcPatientPortal.Areas.Admin.Services;

public static class ImportParser
{
    public const int MaxBytes = 20 * 1024 * 1024;
    public const int MaxRows = 10000;
    public static List<ImportRow> Parse(byte[] content, string format, string template)
    {
        if (content.Length == 0 || content.Length > MaxBytes) throw new ImportRejectedException("Upload a non-empty file of at most 20 MB.");
        try
        {
            return format switch
            {
                "csv" when ImportTemplates.Get(template).Sheet == template => Csv(content, ImportTemplates.Get(template)),
                "xlsx" when template == "Workbook_v1" => Workbook(content),
                _ => throw new ImportRejectedException("Use a UTF-8 CSV template or a version 1 .xlsx workbook.")
            };
        }
        catch (Exception error) when (error is IOException or XmlException or DecoderFallbackException or MalformedLineException or NotSupportedException or OverflowException or ArgumentException or ExcelDataReader.Exceptions.ExcelReaderException)
        { throw new ImportRejectedException("The file is malformed or uses an unsupported encoding or workbook format."); }
    }

    private static List<ImportRow> Csv(byte[] content, ImportTemplate template)
    {
        using var stream = new MemoryStream(content);
        using var parser = new TextFieldParser(stream, new UTF8Encoding(false, true), false) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var header = parser.ReadFields() ?? [];
        if (header.Length > 0) header[0] = header[0].TrimStart('\uFEFF');
        Header(header, template);
        var rows = new List<ImportRow>();
        while (!parser.EndOfData)
        {
            var number = checked((int)parser.LineNumber);
            Add(rows, template, number, parser.ReadFields() ?? []);
        }
        if (rows.Count == 0) throw new ImportRejectedException("The template contains no data rows.");
        return rows;
    }

    public static void CheckWorkbook(byte[] content)
    {
        if (content.Length < 4 || content[0] != 'P' || content[1] != 'K' || content[2] != 3 || content[3] != 4)
            throw new ImportRejectedException("Encrypted, OLE and non-OpenXML workbooks are not accepted.");
        using var stream = new MemoryStream(content);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 256 || zip.Entries.Sum(entry => entry.Length) > 80L * 1024 * 1024)
            throw new ImportRejectedException("The workbook exceeds the archive safety limits.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (!names.Add(entry.FullName) || entry.FullName.Contains("..", StringComparison.Ordinal) || entry.FullName.Contains('\\') || entry.FullName.StartsWith('/') || entry.Length > 40L * 1024 * 1024)
                throw new ImportRejectedException("The workbook has unsafe archive entries.");
            if (entry.FullName.Contains("externalLink", StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains("vba", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                throw new ImportRejectedException("Macros and external links are not accepted.");
            if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) continue;
            using var part = entry.Open();
            using var xml = XmlReader.Create(part, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 40L * 1024 * 1024 });
            while (xml.Read())
            {
                if (xml.NodeType != XmlNodeType.Element) continue;
                if (xml.LocalName == "f" || xml.LocalName.Contains("Formula", StringComparison.OrdinalIgnoreCase) || xml.LocalName == "definedName")
                    throw new ImportRejectedException("Formula cells and defined-name formulas are not accepted. Export values only.");
                if (xml.GetAttribute("TargetMode")?.Equals("External", StringComparison.OrdinalIgnoreCase) == true ||
                    xml.GetAttribute("ContentType")?.Contains("macro", StringComparison.OrdinalIgnoreCase) == true)
                    throw new ImportRejectedException("Macros and external links are not accepted.");
            }
        }
    }

    private static List<ImportRow> Workbook(byte[] content)
    {
        CheckWorkbook(content);
        // ExcelDataReader's configuration constructor requests code page 1252, even for OpenXML.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = new MemoryStream(content);
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
        var rows = new List<ImportRow>(); var sheets = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var template = ImportTemplates.All.SingleOrDefault(t => t.Sheet == reader.Name)
                ?? throw new ImportRejectedException("The workbook contains an unknown sheet.");
            if (!sheets.Add(reader.Name) || !reader.Read()) throw new ImportRejectedException("Each sheet needs exactly one template header.");
            Header(Enumerable.Range(0, reader.FieldCount).Select(i => Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "").ToArray(), template);
            var number = 1;
            while (reader.Read())
            {
                number++;
                if (Enumerable.Range(0, reader.FieldCount).All(reader.IsDBNull)) continue;
                var values = new string[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    if (value is not null && template.Columns[i] is "SourcePatientKey" or "SourceRecordKey" or "SourceLabKey" or "SourcePrescriptionKey" or "SourceEncounterKey" or "HospitalNumber" && value is not string)
                        throw new ImportRejectedException("Identifiers and hospital numbers must be text cells to retain leading zeros.");
                    values[i] = value is DateTime date ? date.ToString(template.Columns[i] == "DoseTime" ? "HH:mm:ss.fffffff" : template.Columns[i] is "DateOfBirth" or "SourceBirthDate" ? "yyyy-MM-dd" : "yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture) : value is bool boolean ? boolean.ToString() : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                }
                Add(rows, template, number, values);
            }
        } while (reader.NextResult());
        if (rows.Count == 0) throw new ImportRejectedException("The workbook contains no data rows.");
        return rows;
    }

    private static void Header(string[] header, ImportTemplate template)
    {
        if (!header.SequenceEqual(template.Columns, StringComparer.Ordinal))
            throw new ImportRejectedException("Columns must exactly match the selected version 1 template, in order. Unknown or Identity columns are not accepted.");
    }

    private static void Add(List<ImportRow> rows, ImportTemplate template, int number, string[] values)
    {
        if (values.Length != template.Columns.Length || values.Any(value => value.Length > 100000))
            throw new ImportRejectedException("A row has the wrong column count or a field exceeds 100,000 characters.");
        if (rows.Count >= MaxRows) throw new ImportRejectedException("A batch may contain at most 10,000 data rows across all sheets.");
        rows.Add(new(template.Name, number, template.Columns.Zip(values).ToDictionary(pair => pair.First, pair => pair.Second)));
    }

    public static byte[] ExampleWorkbook()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            const string spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            const string relationship = "http://schemas.openxmlformats.org/package/2006/relationships";
            void Part(string path, Action<XmlWriter> write)
            {
                using var target = zip.CreateEntry(path).Open();
                using var xml = XmlWriter.Create(target, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
                write(xml);
            }
            Part("[Content_Types].xml", xml =>
            {
                xml.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                void Type(string element, string key, string value, string contentType) { xml.WriteStartElement(element); xml.WriteAttributeString(key, value); xml.WriteAttributeString("ContentType", contentType); xml.WriteEndElement(); }
                Type("Default", "Extension", "rels", "application/vnd.openxmlformats-package.relationships+xml");
                Type("Default", "Extension", "xml", "application/xml");
                Type("Override", "PartName", "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                for (var i = 1; i <= ImportTemplates.All.Count; i++) Type("Override", "PartName", "/xl/worksheets/sheet" + i + ".xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                xml.WriteEndElement();
            });
            Part("_rels/.rels", xml => { xml.WriteStartElement("Relationships", relationship); xml.WriteStartElement("Relationship"); xml.WriteAttributeString("Id", "rId1"); xml.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"); xml.WriteAttributeString("Target", "xl/workbook.xml"); xml.WriteEndElement(); xml.WriteEndElement(); });
            Part("xl/workbook.xml", xml =>
            {
                xml.WriteStartElement("workbook", spreadsheet); xml.WriteStartElement("sheets");
                for (var i = 0; i < ImportTemplates.All.Count; i++) { xml.WriteStartElement("sheet"); xml.WriteAttributeString("name", ImportTemplates.All[i].Sheet); xml.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture)); xml.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", "rId" + (i + 1)); xml.WriteEndElement(); }
                xml.WriteEndElement(); xml.WriteEndElement();
            });
            Part("xl/_rels/workbook.xml.rels", xml =>
            {
                xml.WriteStartElement("Relationships", relationship);
                for (var i = 1; i <= ImportTemplates.All.Count; i++) { xml.WriteStartElement("Relationship"); xml.WriteAttributeString("Id", "rId" + i); xml.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"); xml.WriteAttributeString("Target", "worksheets/sheet" + i + ".xml"); xml.WriteEndElement(); }
                xml.WriteEndElement();
            });
            for (var i = 0; i < ImportTemplates.All.Count; i++)
            {
                var template = ImportTemplates.All[i];
                Part("xl/worksheets/sheet" + (i + 1) + ".xml", xml =>
                {
                    xml.WriteStartElement("worksheet", spreadsheet); xml.WriteStartElement("sheetData");
                    var number = 1;
                    foreach (var row in new[] { template.Columns }.Concat(ImportExamples.Rows(template, true)))
                    {
                        xml.WriteStartElement("row"); xml.WriteAttributeString("r", (number++).ToString(CultureInfo.InvariantCulture));
                        foreach (var value in row) { xml.WriteStartElement("c"); xml.WriteAttributeString("t", "inlineStr"); xml.WriteStartElement("is"); xml.WriteElementString("t", value); xml.WriteEndElement(); xml.WriteEndElement(); }
                        xml.WriteEndElement();
                    }
                    xml.WriteEndElement(); xml.WriteEndElement();
                });
            }
        }
        return stream.ToArray();
    }
}
