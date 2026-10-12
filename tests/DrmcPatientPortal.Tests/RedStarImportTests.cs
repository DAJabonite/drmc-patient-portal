using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DrmcPatientPortal.Tests;

// Every row is synthetic. The client's source workbook must never become a fixture.
public sealed class RedStarImportTests
{
    private static byte[] Living(params string[][] rows) => RedStarRegistryParser.Workbook(new[] { ("LIVING", new[]
    { new[] { "LEGEND", "NAME", "Patient Code", "Code Name", "HRN", "REFILL DATE", "Next Refill Date", "Last Contact No. Provided", "REGIMEN", "", "", "TREATMENT STATUS", "Current Age", "UIC" } }.Concat(rows).ToArray()) });

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
    private static byte[] EditSheet(byte[] original, Action<XDocument> change)
    {
        using var bytes = new MemoryStream(); bytes.Write(original);
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Update, true))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!; XDocument xml;
            using (var stream = entry.Open()) xml = XDocument.Load(stream);
            change(xml); entry.Delete(); using var target = zip.CreateEntry("xl/worksheets/sheet1.xml").Open(); xml.Save(target);
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
                .Include(x => x.Enrollments).ThenInclude(x => x.Dispenses).SingleAsync(x => x.PatientCode == code);
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
        var patientId = await Db(db => db.RedStarPatients.Where(x => x.PatientCode == code).Select(x => x.Id).SingleAsync());
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
            var appointment = await db.RedStarAppointments.Include(x => x.Enrollment).ThenInclude(x => x.Patient).SingleAsync(x => x.Enrollment.Patient.PatientCode == code);
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
        var patientId = await Db(db => db.RedStarPatients.Where(x => x.PatientCode == code).Select(x => x.Id).SingleAsync());
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
        Assert.False(await Db(db => db.RedStarPatients.AnyAsync(x => x.PatientCode == newCode)));
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
        Assert.Equal(RedStarStatus.AwaitingVerification, await Db(db => db.RedStarEnrollments.Where(x => x.Patient.PatientCode == code).Select(x => x.Status).SingleAsync()));
    }
}
