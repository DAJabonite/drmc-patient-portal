using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DrmcPatientPortal.Areas.Admin.Controllers;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DrmcPatientPortal.Tests;

// Every row is synthetic. The client's source workbook must never become a fixture.
public sealed class RedStarImportTests
{
    private static byte[] Living(params string[][] rows) => RedStarRegistryParser.Workbook(new[] { ("LIVING", new[]
    { new[] { "LEGEND", "NAME", "Patient Code", "Code Name", "HRN", "REFILL DATE", "Next Refill Date", "Last Contact No. Provided", "REGIMEN", "", "", "TREATMENT STATUS", "Current Age", "UIC" } }.Concat(rows).ToArray()) });

    private static byte[] Csv(params string[][] rows) => Encoding.UTF8.GetBytes(string.Join("\r\n", rows.Select(row =>
        string.Join(',', row.Select(value => "\"" + value.Replace("\"", "\"\"") + "\"")))) + "\r\n");

    [Fact]
    public void Template_download_has_exact_flat_headers_two_fictional_rows_and_valid_dates()
    {
        var file = Assert.IsType<FileContentResult>(new RedStarImportsController(null!, null!).DownloadTemplate());
        Assert.Equal("redstar-import-template.csv", file.FileDownloadName);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.True(file.FileContents.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        var text = new UTF8Encoding(false, true).GetString(file.FileContents.AsSpan(3));
        Assert.Equal("NAME,Address*,HRN,Patient Code,Code Name,Enrolment/Baseline Date,ART Start Date @ RED STAR,REFILL DATE,Next Refill Date,Last Contact No. Provided,REGIMEN,Status,Program,Status Date,Receiving Hub", text.Split("\r\n")[0]);
        Assert.Equal(3, text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.DoesNotContain('\n', text.Replace("\r\n", ""));
        var preview = RedStarRegistryParser.Parse(file.FileContents, RedStarImportFormat.Csv);
        Assert.Equal(2, preview.Rows.Count);
        Assert.All(preview.Rows, row =>
        {
            Assert.StartsWith("Synthetic Patient", row.FullName);
            Assert.Equal(RedStarProgram.ART, row.Program);
            Assert.DoesNotContain(row.Warnings, warning => warning.Contains("could not be read") || warning.Contains("Ambiguous"));
        });
        Assert.Equal("0000123", preview.Rows[0].HospitalNumber);
        Assert.Equal(RedStarStatus.Active, preview.Rows[0].Status);
        Assert.Equal(new DateTime(2026, 10, 31), preview.Rows[0].NextRefillDate);
        Assert.Equal("Synthetic Patient Niño", preview.Rows[1].FullName);
        Assert.Equal("Sample \"B\" Street, Fictional City", preview.Rows[1].Address);
        Assert.Equal(RedStarStatus.Deceased, preview.Rows[1].Status);
        Assert.Equal(new DateTime(2026, 10, 15), preview.Rows[1].StatusDate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Csv_preserves_quoted_unicode_text_identifiers_and_multiline_fields_with_or_without_bom(bool bom)
    {
        var content = Csv(
            ["NAME", "Patient Code", "HRN", "Address*", "REGIMEN", "Program", "Status", "REFILL DATE"],
            ["  Synthetic  Niño (Jr.)  ", "000007", "0000123", "  Sample \"B\" Street,\r\n  City (North)  ", "Medicine A (300 mg)", "ART", "Living", "2026-10-01 (confirmed)"]);
        if (bom) content = Encoding.UTF8.GetPreamble().Concat(content).ToArray();
        var preview = RedStarRegistryParser.Parse(content, RedStarImportFormat.Csv);
        var row = Assert.Single(preview.Rows);
        Assert.Equal(RedStarImportFormat.Csv, preview.SourceFormat);
        Assert.Equal("CSV:2", row.Key);
        Assert.Equal("Synthetic Niño (Jr.)", row.FullName);
        Assert.Equal("000007", row.PatientCode);
        Assert.Equal("0000123", row.HospitalNumber);
        Assert.Equal("Sample \"B\" Street, City (North)", row.Address);
        Assert.Equal("Medicine A (300 mg)", row.Medicines);
        Assert.Equal(RedStarStatus.Active, row.Status);
        Assert.Equal(new DateTime(2026, 10, 1), row.DispensedDate);
    }

    [Theory]
    [InlineData(RedStarImportFormat.Xlsx)]
    [InlineData(RedStarImportFormat.Csv)]
    public void Structured_annotations_and_whitespace_are_cleaned_without_changing_free_text(RedStarImportFormat format)
    {
        string[][] rows =
        [
            ["NAME", "Patient Code", "Code Name", "HRN", "Contact Number", "Current Address", "Receiving Hub", "REGIMEN", "Program", "Status", "Enrolled Date", "Start Date", "REFILL DATE", "Next Refill Date"],
            ["  Synthetic\t Person (Jr.)  ", "CODE-001 (duplicate (review)) (confirmed)", "ALPHA (legacy)", "0000456 (old)", "(0917) 123-4567 (relative 42)", " Zone\u00a0 1 (North) ", " Sample  Hub (East) ", " Medicine A (300 mg)  /  Medicine B (night) ", " ART (verified) ", " Active (telephone review) ", "2026-01-01 (baseline)", "1/2/2026 (verified)", "2026-10-01 (pickup)", "2026-10-31 (next)"]
        ];
        var content = format == RedStarImportFormat.Csv ? Csv(rows) : RedStarRegistryParser.Workbook(new[] { ("Living", rows) });
        var row = Assert.Single(RedStarRegistryParser.Parse(content, format).Rows);
        Assert.Equal("Synthetic Person (Jr.)", row.FullName);
        Assert.Equal("CODE-001", row.PatientCode);
        Assert.Equal("ALPHA", row.CodeName);
        Assert.Equal("0000456", row.HospitalNumber);
        Assert.Equal("+639171234567", row.ContactNumber);
        Assert.Equal("Zone 1 (North)", row.Address);
        Assert.Equal("Sample Hub (East)", row.ReceivingHub);
        Assert.Equal("Medicine A (300 mg) / Medicine B (night)", row.Medicines);
        Assert.Equal(RedStarProgram.ART, row.Program);
        Assert.Equal(RedStarStatus.Active, row.Status);
        Assert.Equal("Active", row.SourceStatus);
        Assert.Equal(new DateTime(2026, 1, 1), row.EnrolledDate);
        Assert.Equal(new DateTime(2026, 1, 2), row.StartDate);
        Assert.Equal(new DateTime(2026, 10, 1), row.DispensedDate);
        Assert.Equal(new DateTime(2026, 10, 31), row.NextRefillDate);
        Assert.DoesNotContain(row.Warnings, warning => warning.Contains("could not be read") || warning.Contains("Ambiguous") || warning.Contains("Contact number"));
    }

    [Theory]
    [InlineData("10/15/2026")]
    [InlineData("2026-10-15")]
    public void Status_date_inside_a_trailing_annotation_is_extracted_without_overriding_an_explicit_date(string date)
    {
        var preview = RedStarRegistryParser.Parse(Csv(
            ["NAME", "Patient Code", "Program", "Status", "Status Date"],
            ["Synthetic Expired Person", "EXPIRED-1", "ART", "Expired (" + date + ", verified)", ""],
            ["Synthetic Dated Person", "EXPIRED-2", "ART", "Expired (" + date + ")", "2026-10-14 (confirmed)"]), RedStarImportFormat.Csv);
        Assert.All(preview.Rows, row => Assert.Equal(RedStarStatus.Deceased, row.Status));
        Assert.Equal("Expired", preview.Rows[0].SourceStatus);
        Assert.Equal(new DateTime(2026, 10, 15), preview.Rows[0].StatusDate);
        Assert.Contains(preview.Rows[0].Warnings, warning => warning.Contains("status date was extracted"));
        Assert.Equal(new DateTime(2026, 10, 14), preview.Rows[1].StatusDate);
    }

    [Theory]
    [InlineData("", "", RedStarProgram.Unverified, RedStarStatus.AwaitingVerification)]
    [InlineData("ART", "", RedStarProgram.ART, RedStarStatus.AwaitingVerification)]
    [InlineData("", "Active", RedStarProgram.Unverified, RedStarStatus.AwaitingVerification)]
    [InlineData("PrEP", "Active", RedStarProgram.PrEP, RedStarStatus.Active)]
    public void Csv_missing_program_or_status_requires_reconciliation(string program, string status, RedStarProgram expectedProgram, RedStarStatus expectedStatus)
    {
        var row = Assert.Single(RedStarRegistryParser.Parse(Csv(["NAME", "Patient Code", "Program", "Status"],
            ["Synthetic CSV Review", "CSV-REVIEW", program, status]), RedStarImportFormat.Csv).Rows);
        Assert.Equal(expectedProgram, row.Program);
        Assert.Equal(expectedStatus, row.Status);
        if (program.Length == 0) Assert.Contains(row.Warnings, warning => warning.Contains("No program"));
        if (status.Length == 0) Assert.Contains(row.Warnings, warning => warning.Contains("No status"));
    }

    [Fact]
    public void Csv_without_program_or_status_columns_and_invalid_values_remains_reviewable()
    {
        var missing = Assert.Single(RedStarRegistryParser.Parse(Csv(["NAME", "Patient Code"],
            ["Synthetic Missing Columns", "CSV-MISSING"]), RedStarImportFormat.Csv).Rows);
        Assert.Equal(RedStarProgram.Unverified, missing.Program);
        Assert.Equal(RedStarStatus.AwaitingVerification, missing.Status);
        var invalid = Assert.Single(RedStarRegistryParser.Parse(Csv(["NAME", "Patient Code", "Program", "Status", "Start Date"],
            ["Synthetic Invalid Fields", "CSV-INVALID", "unknown (review)", "Active (unfinished", "not a date (review)"]), RedStarImportFormat.Csv).Rows);
        Assert.Equal("Active (unfinished", invalid.SourceStatus);
        Assert.Null(invalid.StartDate);
        Assert.Contains(invalid.Warnings, warning => warning.Contains("Unknown program"));
        Assert.Contains(invalid.Warnings, warning => warning.Contains("Ambiguous source status"));
        Assert.Contains(invalid.Warnings, warning => warning.Contains("date could not be read"));
    }

    [Fact]
    public void Living_active_and_expired_tabs_infer_blank_or_absent_status_and_report_unrelated_tabs()
    {
        var bytes = RedStarRegistryParser.Workbook(new[]
        {
            ("Instructions", new[] { new[] { "Synthetic workbook instructions" } }),
            ("Living", new[] { new[] { "NAME", "Patient Code" }, new[] { "Synthetic Living Person", "TAB-LIVING" } }),
            (" aCtIvE ", new[] { new[] { "NAME", "Patient Code", "Status" }, new[] { "Synthetic Active Person", "TAB-ACTIVE", "  " } }),
            ("Expired", new[] { new[] { "NAME", "Patient Code", "Status" }, new[] { "Synthetic Expired Person", "TAB-EXPIRED", "" } }),
            ("Summary", new[] { new[] { "Synthetic totals" } })
        });
        var preview = RedStarRegistryParser.Parse(bytes);
        Assert.Equal(new[] { "Instructions", "Summary" }, preview.SkippedSheets);
        Assert.Equal(new[] { "Living", " aCtIvE ", "Expired" }, preview.Rows.Select(row => row.Sheet));
        Assert.Equal(new[] { RedStarStatus.Active, RedStarStatus.Active, RedStarStatus.Deceased }, preview.Rows.Select(row => row.Status));
        Assert.Equal(3, preview.Rows.Select(row => row.Key).Distinct().Count());
        Assert.All(preview.Rows, row => { Assert.Equal(2, row.Number); Assert.Equal(RedStarProgram.ART, row.Program); Assert.Null(row.SourceStatus); });
        Assert.Equal("LIVING", RedStarRegistryLayout.Find(" aCtIvE ")!.Name);
    }

    [Theory]
    [InlineData("Living", "Expired", RedStarStatus.Deceased)]
    [InlineData("Expired", "Living", RedStarStatus.Active)]
    [InlineData("Active", "Needs review", RedStarStatus.AwaitingVerification)]
    public void Explicit_status_overrides_the_sheet_without_hiding_ambiguous_values(string sheet, string status, RedStarStatus expected)
    {
        var row = Assert.Single(RedStarRegistryParser.Parse(RedStarRegistryParser.Workbook(new[]
        { (sheet, new[] { new[] { "NAME", "Patient Code", "Status" }, new[] { "Synthetic Status Override", "OVERRIDE", status } }) })).Rows);
        Assert.Equal(expected, row.Status);
        Assert.Equal(status, row.SourceStatus);
        if (expected == RedStarStatus.AwaitingVerification) Assert.Contains(row.Warnings, warning => warning.Contains("Ambiguous"));
    }

    [Fact]
    public void Duplicate_codes_are_reconciled_across_tabs_after_annotations_are_removed()
    {
        var preview = RedStarRegistryParser.Parse(RedStarRegistryParser.Workbook(new[]
        {
            ("Living", new[] { new[] { "NAME", "Patient Code" }, new[] { "Synthetic Duplicate Alpha", "DUP-TABS" } }),
            ("Active", new[] { new[] { "NAME", "Patient Code" }, new[] { "Synthetic Duplicate Beta", "DUP-TABS (legacy)" } })
        }));
        Assert.Equal(2, preview.Rows.Count);
        Assert.All(preview.Rows, row => Assert.Contains(row.Warnings, warning => warning.Contains("more than once")));
    }

    [Theory]
    [InlineData(" Active ")]
    [InlineData(" Living ")]
    public void Formula_checks_use_original_sheet_names_even_when_aliases_or_whitespace_are_present(string sheet)
    {
        var bytes = RedStarRegistryParser.Workbook(new[]
        { (sheet, new[] { new[] { "NAME", "Patient Code" }, new[] { "Synthetic Formula Patient", "FORMULA-CODE" } }) });
        Assert.Throws<ImportRejectedException>(() => RedStarRegistryParser.Parse(EditSheet(bytes, doc => SetFormula(doc, "B2"))));
    }

    [Theory]
    [InlineData(RedStarImportFormat.Xlsx)]
    [InlineData(RedStarImportFormat.Csv)]
    public void Patient_limit_is_enforced_across_all_tabs_and_for_flat_csv(RedStarImportFormat format)
    {
        byte[] Content(int count)
        {
            var rows = Enumerable.Range(0, count).Select(index => new[] { "Synthetic Limit Person", "LIMIT-" + index }).ToArray();
            string[] headers = ["NAME", "Patient Code"];
            return format == RedStarImportFormat.Csv ? Csv(new[] { headers }.Concat(rows).ToArray())
                : RedStarRegistryParser.Workbook(new[]
                {
                    ("Living", new[] { headers }.Concat(rows.Take(count / 2)).ToArray()),
                    ("Active", new[] { headers }.Concat(rows.Skip(count / 2)).ToArray())
                });
        }
        Assert.Equal(RedStarRegistryParser.MaxRows, RedStarRegistryParser.Parse(Content(RedStarRegistryParser.MaxRows), format).Rows.Count);
        Assert.Throws<ImportRejectedException>(() => RedStarRegistryParser.Parse(Content(RedStarRegistryParser.MaxRows + 1), format));
    }

    [Theory]
    [InlineData(RedStarImportFormat.Xlsx)]
    [InlineData(RedStarImportFormat.Csv)]
    public void Column_and_byte_limits_apply_to_both_import_formats(RedStarImportFormat format)
    {
        byte[] Content(int count)
        {
            var headers = new[] { "NAME" }.Concat(Enumerable.Range(1, count - 1).Select(index => "Deferred " + index)).ToArray();
            string[][] rows = [headers, ["Synthetic Wide Patient"]];
            return format == RedStarImportFormat.Csv ? Csv(rows) : RedStarRegistryParser.Workbook(new[] { ("Living", rows) });
        }
        Assert.Single(RedStarRegistryParser.Parse(Content(128), format).Rows);
        Assert.Throws<ImportRejectedException>(() => RedStarRegistryParser.Parse(Content(129), format));
        Assert.Throws<ImportRejectedException>(() => RedStarRegistryParser.Parse(new byte[RedStarRegistryParser.MaxBytes + 1], format));
    }

    [Fact]
    public void Csv_rejects_invalid_utf8_and_missing_patient_headers()
    {
        Assert.Throws<ImportRejectedException>(() => RedStarRegistryParser.Parse([0xff, 0xfe, 0x00], RedStarImportFormat.Csv));
        Assert.Throws<ImportRejectedException>(() => RedStarRegistryParser.Parse(Csv(["Unrelated header"], ["Synthetic content"]), RedStarImportFormat.Csv));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Excel_notes_and_threaded_comments_do_not_become_patient_values(bool threaded)
    {
        var original = Living(["", "Synthetic Comment Person", "COMMENT-001", "", "", "2026-10-01 (confirmed)", "", "", "Medicine A (300 mg)", "", "", "Active (reviewed)"]);
        var commented = WithComment(original, threaded);
        var row = Assert.Single(RedStarRegistryParser.Parse(commented).Rows);
        Assert.Equal("Synthetic Comment Person", row.FullName);
        Assert.Equal("COMMENT-001", row.PatientCode);
        Assert.Equal("Medicine A (300 mg)", row.Medicines);
        Assert.Equal(RedStarStatus.Active, row.Status);
        Assert.Equal(new DateTime(2026, 10, 1), row.DispensedDate);
        Assert.DoesNotContain("COMMENT-METADATA-ONLY", JsonSerializer.Serialize(row));
        Assert.Throws<ImportRejectedException>(() => RedStarRegistryParser.Parse(AddPart(commented, "xl/unsafe.rels",
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"unsafe\" Type=\"test\" Target=\"https://example.test\" TargetMode=\"External\"/></Relationships>")));
    }

    [Fact]
    public void Encrypted_previews_created_before_csv_support_still_read_as_xlsx()
    {
        var protection = new EphemeralDataProtectionProvider();
        var batch = new RedStarImportBatch
        {
            ProtectedPreview = protection.CreateProtector("DRMC.RedStar.ImportPreview.v1").Protect(Encoding.UTF8.GetBytes(
                "{\"Rows\":[],\"DeferredColumns\":[],\"IgnoredRows\":2,\"Choices\":[]}"))
        };
        var preview = new RedStarImports(protection, null!).Read(batch);
        Assert.Equal(RedStarImportFormat.Xlsx, preview.SourceFormat);
        Assert.Empty(preview.SkippedSheets);
        Assert.Equal(2, preview.IgnoredRows);
    }

    [Fact]
    public void Mixed_headers_regimen_components_dates_and_phone_are_preserved()
    {
        var preview = RedStarRegistryParser.Parse(Living(new[] { "", "Synthetic Person Alpha", "SYN-001", "ALPHA", "0000123", "2026-10-01", "2026-10-31", "9171234567", "Medicine A", "Medicine B", "Medicine C", "ACTIVE", "31", "DEFERRED-SYNTHETIC" }));
        var row = Assert.Single(preview.Rows);
        Assert.Equal("0000123", row.HospitalNumber);
        Assert.Equal("+639171234567", row.ContactNumber);
        Assert.Equal("Medicine A / Medicine B / Medicine C", row.Medicines);
        Assert.Equal(new DateTime(2026, 10, 1), row.DispensedDate);
        Assert.Equal(new DateTime(2026, 10, 31), row.NextRefillDate);
        Assert.Equal(RedStarStatus.Active, row.Status);
        Assert.Contains("LIVING: UIC", preview.DeferredColumns);
        Assert.Contains(row.Warnings, x => x.Contains("inactive for reminders"));
    }

    [Fact]
    public void Blank_formatted_and_explanatory_rows_are_ignored_without_losing_data_rows()
    {
        var preview = RedStarRegistryParser.Parse(Living(
            new string[14],
            new[] { "", "Reminder: 7 days before refill then monthly reminder", "", "", "", "", "", "", "", "", "", "", "", "" },
            new[] { "", "Synthetic Person Beta", "SYN-002", "", "", "", "2026-11-15", "", "", "", "", "ACTIVE", "", "" },
            new string[14]));
        var row = Assert.Single(preview.Rows);
        Assert.Equal(4, row.Number);
        Assert.Equal(3, preview.IgnoredRows);
    }

    [Fact]
    public void Headerless_rhivda_includes_first_name_and_never_infers_test_or_treatment()
    {
        var bytes = RedStarRegistryParser.Workbook(new[] { ("RHIVDA 2025", new[] { new[] { "Synthetic Review Alpha" }, new[] { "Synthetic Review Beta" } }) });
        var preview = RedStarRegistryParser.Parse(bytes);
        Assert.Equal(2, preview.Rows.Count);
        Assert.Equal(1, preview.Rows[0].Number);
        Assert.All(preview.Rows, x => { Assert.Equal(RedStarProgram.Unverified, x.Program); Assert.Equal(RedStarStatus.AwaitingVerification, x.Status); Assert.Null(x.NextRefillDate); });
        var batch = new RedStarImportBatch();
        var choices = preview.Rows.Select(x => RedStarImports.DefaultChoice(batch, x)).ToArray();
        Assert.All(choices, x => Assert.False(x.Include));
        Assert.NotEqual(choices[0].PatientCode, choices[1].PatientCode);
    }

    [Fact]
    public void Duplicate_private_codes_and_ambiguous_statuses_require_reconciliation()
    {
        var preview = RedStarRegistryParser.Parse(Living(
            new[] { "", "Synthetic Person Alpha", "DUP-SYN", "", "", "", "", "", "", "", "", "ART D/C 1/6/2023", "", "" },
            new[] { "", "Synthetic Person Beta", "DUP-SYN", "", "", "", "", "invalid contact", "", "", "", "Needs review", "", "" }));
        Assert.All(preview.Rows, x => Assert.Contains(x.Warnings, warning => warning.Contains("more than once")));
        Assert.Equal(RedStarStatus.Discontinued, preview.Rows[0].Status);
        Assert.Equal(new DateTime(2023, 1, 6), preview.Rows[0].StatusDate);
        Assert.Equal("ART D/C 1/6/2023", preview.Rows[0].SourceStatus);
        Assert.Equal(RedStarStatus.AwaitingVerification, preview.Rows[1].Status);
        Assert.Contains(preview.Rows[1].Warnings, x => x.Contains("Contact number"));
    }

    [Theory]
    [InlineData("PrEP", RedStarProgram.PrEP)]
    [InlineData("PEP", RedStarProgram.PEP)]
    [InlineData("NB PROPHYLAXIS", RedStarProgram.NewbornProphylaxis)]
    [InlineData("TRANS OUT", RedStarProgram.ART)]
    [InlineData("EXPIRED", RedStarProgram.ART)]
    public void Recognized_sheets_keep_programs_distinct(string sheet, RedStarProgram program)
    {
        var bytes = RedStarRegistryParser.Workbook(new[] { (sheet, new[] { new[] { "Patient Name", "Patient Code", "STATUS", "NEXT REFILL DATE" }, new[] { "Synthetic Course Person", "SYN-COURSE", "", "" } }) });
        var row = Assert.Single(RedStarRegistryParser.Parse(bytes).Rows);
        Assert.Equal(program, row.Program);
        Assert.Null(row.NextRefillDate);
        Assert.Null(row.DispensedDate);
    }

    [Fact]
    public void Formula_in_imported_field_is_rejected_but_deferred_formula_is_never_evaluated()
    {
        var original = Living(new[] { "", "Synthetic Person Alpha", "SYN-001", "", "", "", "", "", "Medicine A", "", "", "ACTIVE", "31", "" });
        var core = EditSheet(original, doc => SetFormula(doc, "I2"));
        Assert.Throws<ImportRejectedException>(() => RedStarRegistryParser.Parse(core));
        var deferred = EditSheet(original, doc => SetFormula(doc, "M2"));
        Assert.Single(RedStarRegistryParser.Parse(deferred).Rows);
    }

    [Fact]
    public void Macros_external_relationships_unsafe_entries_and_unknown_sheets_are_rejected()
    {
        var original = Living(new[] { "", "Synthetic Person Alpha", "SYN-001" });
        Assert.Throws<ImportRejectedException>(() => RedStarRegistryParser.Parse(AddPart(original, "xl/vbaProject.bin", "synthetic")));
        Assert.Throws<ImportRejectedException>(() => RedStarRegistryParser.Parse(AddPart(original, "../outside.xml", "<x/>")));
        Assert.Throws<ImportRejectedException>(() => RedStarRegistryParser.Parse(AddPart(original, "xl/externalLinks/link.xml", "<x/>")));
        var unknown = RedStarRegistryParser.Workbook(new[] { ("Unknown sheet", new[] { new[] { "NAME" }, new[] { "Synthetic Person Alpha" } }) });
        Assert.Throws<ImportRejectedException>(() => RedStarRegistryParser.Parse(unknown));
        Assert.Throws<ImportRejectedException>(() => RedStarRegistryParser.Parse(new byte[RedStarRegistryParser.MaxBytes + 1]));
    }

    [Fact]
    public void Export_uses_literal_strings_for_formula_like_text_and_omits_portal_ownership()
    {
        var enrollment = new RedStarEnrollment
        {
            Patient = new RedStarPatient { FullName = "=SYNTHETIC_FORMULA()", PatientCode = "+SYN-CODE", CodeName = "@SYN", PortalUserId = "PRIVATE-ACCOUNT-NEVER-EXPORT" },
            Program = RedStarProgram.ART, Status = RedStarStatus.Active, Medicines = "=SYNTHETIC_MEDICINE()",
            Dispenses = [new RedStarDispense { DispensedDate = new DateTime(2026, 10, 1) }],
            Appointments = [new RedStarAppointment { Kind = RedStarAppointmentKind.Refill, State = RedStarAppointmentState.Scheduled, DueDate = new DateTime(2026, 10, 31) }]
        };
        var bytes = RedStarRegistryParser.Export([enrollment]);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(stream);
        Assert.DoesNotContain(xml.Descendants(), x => x.Name.LocalName == "f");
        Assert.All(xml.Descendants().Where(x => x.Name.LocalName == "c"), x => Assert.Equal("inlineStr", (string?)x.Attribute("t")));
        Assert.DoesNotContain("PRIVATE-ACCOUNT-NEVER-EXPORT", xml.ToString());
        var row = Assert.Single(RedStarRegistryParser.Parse(bytes).Rows);
        Assert.Equal("=SYNTHETIC_FORMULA()", row.FullName);
        Assert.Equal("+SYN-CODE", row.PatientCode);
        Assert.Equal("=SYNTHETIC_MEDICINE()", row.Medicines);
    }

    [Theory]
    [InlineData(RedStarProgram.ART, RedStarStatus.Active, "LIVING", "C=NAME|G=Address*|H=HRN|I=Patient Code|T=Enrolment/Baseline Date|V=REFILL DATE|Y=REGIMEN|AB=Latest CD4|AD=VIRAL LOAD|AF=TREATMENT STATUS|AI=DATE OF COMPLETION")]
    [InlineData(RedStarProgram.PrEP, RedStarStatus.Active, "PrEP", "C=Patient Name|E=Current Address|G=Patient Code|M=PrEP Start Date @ RED STAR|O=PrEP NEXT Refill Date|Q=PrEP Regimen|R=Date of last HIV Test|Z=STATUS")]
    [InlineData(RedStarProgram.PEP, RedStarStatus.Active, "PEP", "C=Patient Name|E=Hospital No.|F=Patient Code|O=ART Start Date|Q=ARV Regimen|R=STATUS|S=REMARKS")]
    [InlineData(RedStarProgram.NewbornProphylaxis, RedStarStatus.Active, "NB PROPHYLAXIS", "C=Patient Name|E=Hospital No.|F=Patient Code|O=ART Start Date|Q=REFILL DATE|R=NEXT REFILL DATE|S=ARV Regimen|T=STATUS")]
    [InlineData(RedStarProgram.ART, RedStarStatus.TransferredOut, "TRANS OUT", "C=Patient Name|F=Current Address|G=HRN|H=Patient Code|S=Date Enrolled @ RED STAR|X=ARV Regimen|AB=TRANSOUT DATE|AC=Name of Hub*")]
    [InlineData(RedStarProgram.ART, RedStarStatus.Deceased, "EXPIRED", "C=Patient Name|F=Last Address Resided|G=HRN|H=Patient Code|S=Date Enrolled to HACT|V=ARV REGIMEN|Z=LOCATION of Expired|AA=DATE EXPIRED")]
    public void Export_headers_keep_the_staff_workbook_column_positions(RedStarProgram program, RedStarStatus status, string sheet, string positions)
    {
        var bytes = RedStarRegistryParser.Export([SyntheticEnrollment(program, status)]);
        var headers = Headers(bytes, sheet);
        foreach (var specification in positions.Split('|'))
        {
            var pair = specification.Split('=', 2);
            var index = pair[0].Aggregate(0, (value, character) => value * 26 + character - 'A' + 1) - 1;
            Assert.Equal(pair[1], headers[index]);
        }
        Assert.True(headers.Length > 15);
        if (sheet == "LIVING")
        {
            Assert.Equal("", headers[0]);
            Assert.Equal("LEGEND", headers[1]);
            Assert.Equal("", headers[25]);
            Assert.Equal("", headers[26]);
        }
    }

    [Fact]
    public void Export_keeps_unsupported_clinical_cells_blank_and_saved_values_in_their_staff_positions()
    {
        var bytes = RedStarRegistryParser.Export([SyntheticEnrollment(RedStarProgram.ART, RedStarStatus.Active)]);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var row = XDocument.Load(stream).Descendants().Single(element => element.Name.LocalName == "row" && (string?)element.Attribute("r") == "2");
        var cells = row.Elements().ToDictionary(element => element.Attribute("r")!.Value,
            element => string.Concat(element.Descendants().Where(text => text.Name.LocalName == "t").Select(text => text.Value)));
        Assert.Equal("Synthetic Layout Patient", cells["C2"]);
        Assert.Equal("0000123", cells["H2"]);
        Assert.Equal("SYN-LAYOUT", cells["I2"]);
        Assert.Equal("Synthetic Medicine A / Synthetic Medicine B", cells["Y2"]);
        foreach (var address in new[] { "D2", "E2", "F2", "O2", "P2", "AB2", "AC2", "AD2", "AE2", "AG2", "AH2", "AI2" })
            Assert.Equal("", cells[address]);
        Assert.Contains("LIVING: Latest CD4", RedStarRegistryParser.Parse(bytes).DeferredColumns);
    }

    [Theory]
    [InlineData("LIVING", 53, 56, "Program")]
    [InlineData("REACTIVE WHEN TESTED, NONREACTI", 53, 57, "Next Refill Date")]
    [InlineData("TRANS OUT", 53, 56, "Next Refill Date")]
    [InlineData("PrEP", 26, 33, "HRN")]
    [InlineData("NB PROPHYLAXIS", 28, 34, "Address*")]
    [InlineData("PEP", 51, 59, "Address*")]
    [InlineData("RHIVDA 2025", 1, 15, "Address*")]
    [InlineData("EXPIRED", 52, 58, "REFILL DATE")]
    public void Portal_only_columns_follow_the_complete_staff_header_span(string sheet, int staffColumns, int totalColumns, string firstAdditionalHeader)
    {
        var headers = Headers(RedStarRegistryParser.Export([]), sheet);
        Assert.Equal(totalColumns, headers.Length);
        Assert.Equal(firstAdditionalHeader, headers[staffColumns]);
        if (sheet == "REACTIVE WHEN TESTED, NONREACTI")
        {
            Assert.Equal("NAME", headers[2]);
            Assert.Equal("Latest Viral Load (Date/Result)", headers[25]);
            Assert.Equal("STATUS", headers[27]);
        }
    }

    [Theory]
    [InlineData(RedStarProgram.ART, RedStarStatus.Active)]
    [InlineData(RedStarProgram.PrEP, RedStarStatus.Active)]
    [InlineData(RedStarProgram.PEP, RedStarStatus.Active)]
    [InlineData(RedStarProgram.NewbornProphylaxis, RedStarStatus.Active)]
    [InlineData(RedStarProgram.Unverified, RedStarStatus.AwaitingVerification)]
    [InlineData(RedStarProgram.PrEP, RedStarStatus.TransferredOut)]
    [InlineData(RedStarProgram.PEP, RedStarStatus.Deceased)]
    public void Staff_ordered_export_round_trips_all_existing_fields(RedStarProgram program, RedStarStatus status)
    {
        var enrollment = SyntheticEnrollment(program, status);
        var row = Assert.Single(RedStarRegistryParser.Parse(RedStarRegistryParser.Export([enrollment])).Rows);
        Assert.Equal(enrollment.Patient.FullName, row.FullName);
        Assert.Equal(enrollment.Patient.PatientCode, row.PatientCode);
        Assert.Equal(enrollment.Patient.CodeName, row.CodeName);
        Assert.Equal(enrollment.Patient.HospitalNumber, row.HospitalNumber);
        Assert.Equal(enrollment.Patient.Address, row.Address);
        Assert.Equal(enrollment.Patient.ContactNumber, row.ContactNumber);
        Assert.Equal(program, row.Program);
        Assert.Equal(status, row.Status);
        Assert.Equal(enrollment.EnrolledDate, row.EnrolledDate);
        Assert.Equal(enrollment.StartDate, row.StartDate);
        Assert.Equal(enrollment.StatusDate, row.StatusDate);
        Assert.Equal(enrollment.ReceivingHub, row.ReceivingHub);
        Assert.Equal(enrollment.Medicines, row.Medicines);
        Assert.Equal(new DateTime(2026, 10, 1), row.DispensedDate);
        Assert.Equal(new DateTime(2026, 10, 31), row.NextRefillDate);
        // Unsupported staff columns stay in the protected source, not in core patient fields.
    }

    [Fact]
    public void Empty_export_has_supported_headers_for_all_staff_registry_tabs()
    {
        var bytes = RedStarRegistryParser.Export([]);
        foreach (var sheet in new[] { "LIVING", "REACTIVE WHEN TESTED, NONREACTI", "TRANS OUT", "PrEP", "NB PROPHYLAXIS", "PEP", "RHIVDA 2025", "EXPIRED" })
            Assert.True(Headers(bytes, sheet).Length >= 15);
    }

    private static RedStarEnrollment SyntheticEnrollment(RedStarProgram program, RedStarStatus status) => new()
    {
        Patient = new RedStarPatient { FullName = "Synthetic Layout Patient", PatientCode = "SYN-LAYOUT", CodeName = "Synthetic alias",
            HospitalNumber = "0000123", Address = "Synthetic address", ContactNumber = "+639171234567", PortalUserId = "NEVER-EXPORT-OWNERSHIP" },
        Program = program, Status = status, EnrolledDate = new DateTime(2026, 1, 10), StartDate = new DateTime(2026, 1, 12),
        StatusDate = new DateTime(2026, 10, 2), ReceivingHub = "Synthetic receiving hub", Medicines = "Synthetic Medicine A / Synthetic Medicine B",
        Dispenses = [new RedStarDispense { DispensedDate = new DateTime(2026, 9, 1) }, new RedStarDispense { DispensedDate = new DateTime(2026, 10, 1) }],
        Appointments = [new RedStarAppointment { Kind = RedStarAppointmentKind.Refill, State = RedStarAppointmentState.Scheduled, DueDate = new DateTime(2026, 10, 31) }]
    };

    private static string[] Headers(byte[] bytes, string sheet)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var bookStream = zip.GetEntry("xl/workbook.xml")!.Open();
        var book = XDocument.Load(bookStream);
        var tab = Assert.Single(book.Descendants(), element => element.Name.LocalName == "sheet" && (string?)element.Attribute("name") == sheet);
        using var sheetStream = zip.GetEntry("xl/worksheets/sheet" + tab.Attribute("sheetId")!.Value + ".xml")!.Open();
        return XDocument.Load(sheetStream).Descendants().First(element => element.Name.LocalName == "row")
            .Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value).ToArray();
    }

    private static void SetFormula(XDocument doc, string address)
    {
        var cell = doc.Descendants().Single(x => x.Name.LocalName == "c" && (string?)x.Attribute("r") == address);
        cell.RemoveNodes(); cell.SetAttributeValue("t", null);
        cell.Add(new XElement(cell.Name.Namespace + "f", "1+1"), new XElement(cell.Name.Namespace + "v", "2"));
    }
    private static byte[] WithComment(byte[] original, bool threaded)
    {
        const string spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string package = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string modern = "http://schemas.microsoft.com/office/2017/10/relationships";
        var path = threaded ? "xl/threadedComments/threadedComment1.xml" : "xl/comments1.xml";
        var bytes = AddPart(original, path, threaded ? """
            <ThreadedComments xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments">
              <threadedComment ref="F2" personId="{11111111-1111-1111-1111-111111111111}" id="{22222222-2222-2222-2222-222222222222}" dT="2026-10-01T00:00:00Z"><text>COMMENT-METADATA-ONLY: verify privately</text></threadedComment>
            </ThreadedComments>
            """ : """
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors><author>Synthetic reviewer</author></authors>
              <commentList><comment ref="F2" authorId="0"><text><t>COMMENT-METADATA-ONLY: verify privately</t></text></comment></commentList>
            </comments>
            """);
        var relationships = new XElement(XName.Get("Relationships", package), new XElement(XName.Get("Relationship", package),
            new XAttribute("Id", "comment"), new XAttribute("Type", threaded ? modern + "/threadedComment" : office + "/comments"),
            new XAttribute("Target", threaded ? "../threadedComments/threadedComment1.xml" : "../comments1.xml")));
        if (threaded)
        {
            bytes = AddPart(bytes, "xl/persons/person.xml", """
                <personList xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments"><person displayName="Synthetic reviewer" id="{11111111-1111-1111-1111-111111111111}" userId="synthetic@fixtures.test" providerId="None"/></personList>
                """);
            bytes = EditPart(bytes, "xl/_rels/workbook.xml.rels", doc => doc.Root!.Add(new XElement(XName.Get("Relationship", package),
                new XAttribute("Id", "person"), new XAttribute("Type", modern + "/person"), new XAttribute("Target", "persons/person.xml"))));
        }
        else
        {
            bytes = AddPart(bytes, "xl/drawings/vmlDrawing1.vml", """
                <xml xmlns:v="urn:schemas-microsoft-com:vml" xmlns:x="urn:schemas-microsoft-com:office:excel"><v:shape id="_x0000_s1025" type="#_x0000_t202"><x:ClientData ObjectType="Note"><x:Row>1</x:Row><x:Column>5</x:Column></x:ClientData></v:shape></xml>
                """);
            relationships.Add(new XElement(XName.Get("Relationship", package), new XAttribute("Id", "commentDrawing"),
                new XAttribute("Type", office + "/vmlDrawing"), new XAttribute("Target", "../drawings/vmlDrawing1.vml")));
            bytes = EditSheet(bytes, doc => doc.Root!.Add(new XElement(XName.Get("legacyDrawing", spreadsheet),
                new XAttribute(XName.Get("id", office), "commentDrawing"))));
        }
        bytes = AddPart(bytes, "xl/worksheets/_rels/sheet1.xml.rels", relationships.ToString());
        return EditPart(bytes, "[Content_Types].xml", doc =>
        {
            var ns = doc.Root!.Name.Namespace;
            doc.Root.Add(new XElement(ns + "Override", new XAttribute("PartName", "/" + path),
                new XAttribute("ContentType", threaded ? "application/vnd.ms-excel.threadedcomments+xml" : "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml")));
            doc.Root.Add(threaded
                ? new XElement(ns + "Override", new XAttribute("PartName", "/xl/persons/person.xml"), new XAttribute("ContentType", "application/vnd.ms-excel.person+xml"))
                : new XElement(ns + "Default", new XAttribute("Extension", "vml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.vmlDrawing")));
        });
    }

    private static byte[] EditSheet(byte[] original, Action<XDocument> change) => EditPart(original, "xl/worksheets/sheet1.xml", change);

    private static byte[] EditPart(byte[] original, string name, Action<XDocument> change)
    {
        using var bytes = new MemoryStream(); bytes.Write(original);
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Update, true))
        {
            var entry = zip.GetEntry(name)!; XDocument xml;
            using (var stream = entry.Open()) xml = XDocument.Load(stream);
            change(xml); entry.Delete(); using var target = zip.CreateEntry(name).Open(); xml.Save(target);
        }
        return bytes.ToArray();
    }
    private static byte[] AddPart(byte[] original, string name, string content)
    {
        using var bytes = new MemoryStream(); bytes.Write(original);
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Update, true))
        { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(content); }
        return bytes.ToArray();
    }
}

[Collection(PortalCollection.Name)]
public sealed class RedStarImportWorkflowTests(PortalFactory factory)
{
    private Task<HttpClient> Clinic() => StaffAccounts.SignInAsync(factory, "redstar.import.staff@fixtures.test", "RedStarStaff");
    private async Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> action)
    { await using var scope = factory.Services.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()); }
    private async Task<T> Service<T>(Func<RedStarImports, Task<T>> action)
    { await using var scope = factory.Services.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<RedStarImports>()); }
    private Task<RedStarImportBatch> Batch(Guid id) => Db(db => db.RedStarImportBatches.AsNoTracking().SingleAsync(x => x.Id == id));
    private Task<string> Actor() => Db(db => db.Users.Where(x => x.Email == "redstar.import.staff@fixtures.test").Select(x => x.Id).SingleAsync());
    private static byte[] Workbook(string code, bool dates = true) => RedStarRegistryParser.Workbook(new[] { ("LIVING", new[]
    {
        new[] { "NAME", "Patient Code", "REGIMEN", "REFILL DATE", "Next Refill Date", "TREATMENT STATUS" },
        new[] { "Synthetic Import Person " + code, code, dates ? "Synthetic Medicine A" : "", dates ? "2026-10-01" : "", dates ? "2026-10-31" : "", "ACTIVE" }
    }) });
    private async Task<Guid> Stage(byte[] source)
    {
        await Clinic(); var actor = await Actor();
        var staged = await Service(imports => imports.StageAsync(source, actor, default));
        Assert.True(staged.Result.Succeeded, staged.Result.Error); return staged.Id;
    }
    private async Task<WriteResult> Reconcile(Guid id, Action<RedStarImportChoice>? edit = null)
    {
        var batch = await Batch(id); var actor = await Actor();
        return await Service(async imports =>
        {
            var choices = imports.Read(batch).Rows.Select(x => RedStarImports.DefaultChoice(batch, x)).ToList();
            foreach (var choice in choices) { choice.Include = true; edit?.Invoke(choice); }
            return await imports.ReconcileAsync(id, new RedStarReconcileInput { Version = Convert.ToBase64String(batch.RowVersion), Choices = choices }, actor, default);
        });
    }
    private async Task<WriteResult> Approve(Guid id)
    { var batch = await Batch(id); var actor = await Actor(); return await Service(imports => imports.ApproveAsync(id, Convert.ToBase64String(batch.RowVersion), actor, default)); }

    private static async Task<HttpResponseMessage> Upload(HttpClient clinic, byte[] content, string name)
    {
        var page = await clinic.GetStringAsync("/Admin/RedStarImports");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(StaffAccounts.Token(page)), "__RequestVerificationToken");
        form.Add(new ByteArrayContent(content), "file", name);
        return await clinic.PostAsync("/Admin/RedStarImports/Upload", form);
    }

    [Fact]
    public async Task Template_download_requires_clinic_access_and_is_linked_from_the_upload_page()
    {
        var clinic = await Clinic();
        var page = await clinic.GetStringAsync("/Admin/RedStarImports");
        Assert.Contains("Download Sample CSV Template", page);
        Assert.Contains("href=\"/Admin/RedStarImports/DownloadTemplate\"", page);
        Assert.Contains("accept=\".xlsx,.csv\"", page);
        var response = await clinic.GetAsync("/Admin/RedStarImports/DownloadTemplate");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal("redstar-import-template.csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(2, RedStarRegistryParser.Parse(await response.Content.ReadAsByteArrayAsync(), RedStarImportFormat.Csv).Rows.Count);
        Assert.Equal(HttpStatusCode.Redirect, (await factory.CreatePortalClient().GetAsync("/Admin/RedStarImports/DownloadTemplate")).StatusCode);
        var lab = await StaffAccounts.SignInAsync(factory, "redstar.template.lab@fixtures.test", "LabStaff");
        Assert.Equal(HttpStatusCode.Forbidden, (await lab.GetAsync("/Admin/RedStarImports/DownloadTemplate")).StatusCode);
    }

    [Theory]
    [InlineData(RedStarImportFormat.Csv)]
    [InlineData(RedStarImportFormat.Xlsx)]
    public async Task Http_upload_keeps_the_original_format_and_bytes_through_reconciliation_and_approval(RedStarImportFormat format)
    {
        var clinic = await Clinic();
        var code = "IMP-" + Guid.NewGuid().ToString("N");
        var content = format == RedStarImportFormat.Csv
            ? Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("NAME,Patient Code,HRN,Program,Status,REGIMEN,Next Refill Date\r\nSynthetic CSV Patient," + code + ",0000123,ART,Living (confirmed),Sample medicine (300 mg),2026-10-31 (verified)\r\n")).ToArray()
            : Workbook(code);
        var response = await Upload(clinic, content, format == RedStarImportFormat.Csv ? "synthetic.CSV" : "synthetic.xlsx");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/Admin/RedStarImports/Reconcile/", location);
        var id = Guid.Parse(location.Split('/').Last());
        var batch = await Batch(id);
        Assert.NotEqual(content, batch.ProtectedSource);
        Assert.DoesNotContain(code, Encoding.UTF8.GetString(batch.ProtectedPreview));
        Assert.Equal(format, await Service(imports => Task.FromResult(imports.Read(batch).SourceFormat)));
        Assert.False(await Db(db => db.RedStarPatients.AnyAsync(patient => patient.PatientCodeBlindIndex == db.Phi.PatientCodeIndex(code))));
        Assert.True((await Reconcile(id)).Succeeded);
        var saved = await Batch(id);
        Assert.Equal(format, await Service(imports => Task.FromResult(imports.Read(saved).SourceFormat)));
        Assert.True((await Approve(id)).Succeeded);
        var source = await clinic.PostFormAsync(location, "/Admin/RedStarImports/Source/" + id, new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.OK, source.StatusCode);
        Assert.True(source.Headers.CacheControl?.NoStore);
        Assert.Equal(format == RedStarImportFormat.Csv ? "Registry-source.csv" : "Registry-source.xlsx", source.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(format == RedStarImportFormat.Csv ? "text/csv" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", source.Content.Headers.ContentType?.MediaType);
        Assert.Equal(content, await source.Content.ReadAsByteArrayAsync());
        await Db(async db =>
        {
            var enrollment = await db.RedStarEnrollments.Include(enrollment => enrollment.Patient).Include(enrollment => enrollment.Appointments)
                .SingleAsync(enrollment => enrollment.Patient.PatientCodeBlindIndex == db.Phi.PatientCodeIndex(code));
            Assert.Equal(RedStarStatus.Active, enrollment.Status);
            Assert.Null(enrollment.Patient.PortalUserId);
            Assert.False(Assert.Single(enrollment.Appointments).RemindersVerified);
            if (format == RedStarImportFormat.Csv) Assert.Equal("0000123", enrollment.Patient.HospitalNumber);
            return true;
        });
    }

    [Fact]
    public async Task Skipped_tabs_are_displayed_in_the_protected_preview()
    {
        var code = "IMP-" + Guid.NewGuid().ToString("N");
        var content = RedStarRegistryParser.Workbook(new[]
        {
            ("Instructions", new[] { new[] { "Synthetic instructions only" } }),
            ("Active", new[] { new[] { "NAME", "Patient Code" }, new[] { "Synthetic Active Preview", code } })
        });
        var id = await Stage(content);
        var page = await (await Clinic()).GetStringAsync("/Admin/RedStarImports/Reconcile/" + id);
        Assert.Contains("Unrecognized tabs were skipped: Instructions", page);
        Assert.Contains("Active · source row 2", page);
        Assert.DoesNotContain("original workbook", page);
    }

    [Fact]
    public async Task Protected_preview_renders_and_approval_creates_only_unlinked_private_records_with_inactive_schedules()
    {
        var code = "IMP-" + Guid.NewGuid().ToString("N"); var bytes = Workbook(code); var id = await Stage(bytes);
        var batch = await Batch(id);
        Assert.DoesNotContain(code, Encoding.UTF8.GetString(batch.ProtectedPreview));
        Assert.False(bytes.SequenceEqual(batch.ProtectedSource));
        Assert.True(await Service(imports => Task.FromResult(bytes.SequenceEqual(imports.ReadSource(batch)))));
        var clinic = await Clinic(); var page = await clinic.GetAsync("/Admin/RedStarImports/Reconcile/" + id);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode); Assert.True(page.Headers.CacheControl?.NoStore);
        Assert.Contains("Include this row on approval", await page.Content.ReadAsStringAsync());
        var reconcile = await Reconcile(id); Assert.True(reconcile.Succeeded, reconcile.Error);
        var approved = await Approve(id); Assert.True(approved.Succeeded, approved.Error);
        await Db(async db =>
        {
            var patient = await db.RedStarPatients.Include(x => x.Enrollments).ThenInclude(x => x.Appointments)
                .Include(x => x.Enrollments).ThenInclude(x => x.Dispenses).SingleAsync(x => x.PatientCodeBlindIndex == db.Phi.PatientCodeIndex(code));
            Assert.Null(patient.PortalUserId); Assert.False(patient.EmailRemindersEnabled);
            var enrollment = Assert.Single(patient.Enrollments);
            Assert.False(Assert.Single(enrollment.Appointments).RemindersVerified);
            var dispense = Assert.Single(enrollment.Dispenses); Assert.Null(dispense.DaysSupplied); Assert.Null(dispense.EstimatedSupplyEnd);
            Assert.False(await db.PatientRecords.AnyAsync(x => x.FullName == patient.FullName));
            Assert.False(await db.Prescriptions.AnyAsync(x => x.GenericName == enrollment.Medicines));
            Assert.True(await db.RedStarAudits.AnyAsync(x => x.Action == "ImportApprove" && x.Subject == id.ToString("N")));
            Assert.False(await db.AdminAuditLogs.AnyAsync(x => x.RecordKey == id.ToString("N")));
            return true;
        });
        Assert.False((await Approve(id)).Succeeded);
    }

    [Fact]
    public async Task Reimport_requires_explicit_update_and_missing_values_preserve_clinic_history()
    {
        var code = "IMP-" + Guid.NewGuid().ToString("N"); var first = await Stage(Workbook(code));
        Assert.True((await Reconcile(first)).Succeeded); Assert.True((await Approve(first)).Succeeded);
        var patientId = await Db(db => db.RedStarPatients.Where(x => x.PatientCodeBlindIndex == db.Phi.PatientCodeIndex(code)).Select(x => x.Id).SingleAsync());
        var again = await Stage(Workbook(code, false)); Assert.True((await Reconcile(again)).Succeeded);
        Assert.False((await Approve(again)).Succeeded);
        var update = await Reconcile(again, choice => choice.ExistingPatientId = patientId); Assert.True(update.Succeeded, update.Error);
        var approved = await Approve(again); Assert.True(approved.Succeeded, approved.Error);
        await Db(async db =>
        {
            var enrollment = await db.RedStarEnrollments.Include(x => x.Dispenses).Include(x => x.Appointments).SingleAsync(x => x.PatientId == patientId);
            Assert.Equal("Synthetic Medicine A", enrollment.Medicines);
            Assert.Single(enrollment.Dispenses); Assert.Single(enrollment.Appointments);
            Assert.Equal(new DateTime(2026, 10, 31), enrollment.Appointments.Single().DueDate);
            return true;
        });
    }

    [Fact]
    public async Task Explicit_date_reimport_deactivates_verified_schedule_cancels_requests_and_never_duplicates_dispensing()
    {
        var code = "IMP-" + Guid.NewGuid().ToString("N"); var first = await Stage(Workbook(code));
        Assert.True((await Reconcile(first)).Succeeded); Assert.True((await Approve(first)).Succeeded);
        var patientId = await Db(async db =>
        {
            var appointment = await db.RedStarAppointments.Include(x => x.Enrollment).ThenInclude(x => x.Patient).SingleAsync(x => x.Enrollment.Patient.PatientCodeBlindIndex == db.Phi.PatientCodeIndex(code));
            appointment.RemindersVerified = true; appointment.ConfirmedUtc = DateTimeOffset.UtcNow;
            db.RedStarRescheduleRequests.Add(new RedStarRescheduleRequest { Appointment = appointment, AppointmentRevision = appointment.Revision, RequestedDate = appointment.DueDate.AddDays(1), CreatedUtc = DateTimeOffset.UtcNow });
            db.RedStarReminders.Add(new RedStarReminder { Appointment = appointment, AppointmentRevision = appointment.Revision, Channel = RedStarReminderChannel.InApp, Stage = "before7", State = RedStarDeliveryState.Delivered });
            await db.SaveChangesAsync(); return appointment.Enrollment.PatientId;
        });
        var again = await Stage(Workbook(code));
        Assert.True((await Reconcile(again, x => x.ExistingPatientId = patientId)).Succeeded);
        var approved = await Approve(again); Assert.True(approved.Succeeded, approved.Error);
        await Db(async db =>
        {
            var enrollment = await db.RedStarEnrollments.Include(x => x.Dispenses).Include(x => x.Appointments).SingleAsync(x => x.PatientId == patientId);
            Assert.Single(enrollment.Dispenses); var appointment = Assert.Single(enrollment.Appointments);
            Assert.False(appointment.RemindersVerified); Assert.Null(appointment.ConfirmedUtc); Assert.Equal(2, appointment.Revision);
            Assert.Equal(RedStarRequestState.Declined, (await db.RedStarRescheduleRequests.SingleAsync(x => x.AppointmentId == appointment.Id)).State);
            Assert.Equal(RedStarDeliveryState.Cancelled, (await db.RedStarReminders.SingleAsync(x => x.AppointmentId == appointment.Id)).State);
            return true;
        });
    }

    [Fact]
    public async Task Schedule_change_after_reconciliation_rejects_approval_without_overwriting_newer_state()
    {
        var code = "IMP-" + Guid.NewGuid().ToString("N"); var first = await Stage(Workbook(code));
        Assert.True((await Reconcile(first)).Succeeded); Assert.True((await Approve(first)).Succeeded);
        var patientId = await Db(db => db.RedStarPatients.Where(x => x.PatientCodeBlindIndex == db.Phi.PatientCodeIndex(code)).Select(x => x.Id).SingleAsync());
        var again = await Stage(Workbook(code)); Assert.True((await Reconcile(again, x => x.ExistingPatientId = patientId)).Succeeded);
        await Db(async db => { var appointment = await db.RedStarAppointments.SingleAsync(x => x.Enrollment.PatientId == patientId); appointment.DueDate = appointment.DueDate.AddDays(1); appointment.Revision++; await db.SaveChangesAsync(); return true; });
        var approved = await Approve(again); Assert.False(approved.Succeeded); Assert.True(approved.Conflict);
        Assert.Null((await Batch(again)).ApprovedUtc);
        Assert.Equal(new DateTime(2026, 11, 1), await Db(db => db.RedStarAppointments.Where(x => x.Enrollment.PatientId == patientId).Select(x => x.DueDate).SingleAsync()));
    }

    [Fact]
    public async Task Approval_is_atomic_when_a_later_included_row_collides_with_an_existing_private_code()
    {
        var existing = "IMP-" + Guid.NewGuid().ToString("N"); var initial = await Stage(Workbook(existing));
        Assert.True((await Reconcile(initial)).Succeeded); Assert.True((await Approve(initial)).Succeeded);
        var newCode = "IMP-" + Guid.NewGuid().ToString("N");
        var bytes = RedStarRegistryParser.Workbook(new[] { ("LIVING", new[] { new[] { "NAME", "Patient Code" },
            new[] { "Synthetic Atomic Alpha", newCode }, new[] { "Synthetic Atomic Beta", existing } }) });
        var id = await Stage(bytes); Assert.True((await Reconcile(id)).Succeeded);
        Assert.False((await Approve(id)).Succeeded);
        Assert.False(await Db(db => db.RedStarPatients.AnyAsync(x => x.PatientCodeBlindIndex == db.Phi.PatientCodeIndex(newCode))));
        Assert.Null((await Batch(id)).ApprovedUtc);
    }

    [Fact]
    public async Task Name_only_rhivda_cannot_be_approved_as_confirmed_program_or_active_enrollment()
    {
        var id = await Stage(RedStarRegistryParser.Workbook(new[] { ("RHIVDA 2025", new[] { new[] { "Synthetic Diagnostic Review" } }) }));
        var rejected = await Reconcile(id, x => { x.Program = RedStarProgram.ART; x.Status = RedStarStatus.Active; });
        Assert.False(rejected.Succeeded); Assert.Contains("unverified", rejected.Error);
        Assert.True((await Reconcile(id)).Succeeded); Assert.True((await Approve(id)).Succeeded);
        var batch = await Batch(id);
        var code = await Service(imports => Task.FromResult(imports.Read(batch).Choices.Single().PatientCode));
        Assert.Equal(RedStarStatus.AwaitingVerification, await Db(db => db.RedStarEnrollments.Where(x => x.Patient.PatientCodeBlindIndex == db.Phi.PatientCodeIndex(code)).Select(x => x.Status).SingleAsync()));
    }
}
