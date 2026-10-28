using System.Net;
using System.Globalization;
using System.IO.Compression;
using System.Security.Claims;
using System.Xml.Linq;
using DrmcPatientPortal.Areas.Admin.Controllers;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using ExcelDataReader;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed class RedStarStaffTests(PortalFactory factory)
{
    private sealed record Fixture(int PatientId, int EnrollmentId, int AppointmentId, string UserId, string Email, string Code);
    private Task<HttpClient> Clinic() => StaffAccounts.SignInAsync(factory, "redstar.operations@fixtures.test", "RedStarStaff");

    private static RedStarController ReportController(ApplicationDbContext db, IServiceProvider services) => new(db, services.GetRequiredService<AdminWrites>())
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "synthetic-report-staff")], "Test"))
            }
        }
    };

    private static RedStarEnrollment ReportEnrollment(string code, RedStarProgram program, RedStarStatus status, DateTime? statusDate = null) => new()
    {
        Patient = new RedStarPatient { PatientCode = code, CodeName = "Synthetic report code", FullName = "Private synthetic report patient" },
        Program = program, Status = status, StatusDate = statusDate, EnrolledDate = new DateTime(2020, 1, 1)
    };

    private static RedStarRegistryPage RegistryPage(IActionResult result) => Assert.IsType<RedStarRegistryPage>(Assert.IsType<ViewResult>(result).Model);

    [Fact]
    public async Task Monthly_summary_distinguishes_active_programs_from_living_ART_and_stays_registry_wide()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var controller = ReportController(db, scope.ServiceProvider);
        var prefix = "SUMMARY-" + Guid.NewGuid().ToString("N") + "-";
        var before = RegistryPage(await controller.Index(search: prefix, month: "2024-02"));
        foreach (var program in new[] { RedStarProgram.ART, RedStarProgram.PrEP, RedStarProgram.PEP, RedStarProgram.NewbornProphylaxis })
            db.RedStarEnrollments.Add(ReportEnrollment(prefix + program, program, RedStarStatus.Active));
        foreach (var status in new[] { RedStarStatus.AwaitingVerification, RedStarStatus.TransferredOut, RedStarStatus.Discontinued, RedStarStatus.Completed })
            db.RedStarEnrollments.Add(ReportEnrollment(prefix + status, RedStarProgram.ART, status));
        db.RedStarEnrollments.Add(ReportEnrollment(prefix + "expired", RedStarProgram.ART, RedStarStatus.Deceased, new DateTime(2024, 2, 29)));
        db.RedStarEnrollments.Add(ReportEnrollment(prefix + "undated", RedStarProgram.ART, RedStarStatus.Deceased));
        await db.SaveChangesAsync();

        var active = RegistryPage(await controller.Index(search: prefix, status: RedStarRegistryStatusFilter.Active, month: "2024-02"));
        Assert.Equal(4, active.Total);
        Assert.All(active.Rows, row => Assert.Equal(RedStarStatus.Active, row.Status));
        Assert.Equal(before.Summary.ActiveCount + 4, active.Summary.ActiveCount);
        Assert.Equal(before.Summary.LivingCount + 1, active.Summary.LivingCount);
        Assert.Equal(before.Summary.ExpiredThisMonthCount + 1, active.Summary.ExpiredThisMonthCount);
        var living = RegistryPage(await controller.Index(search: prefix, program: RedStarProgram.ART, status: RedStarRegistryStatusFilter.Living, month: "2", year: 2024));
        Assert.Equal(RedStarProgram.ART, Assert.Single(living.Rows).Program);
        Assert.Equal(active.Summary, living.Summary);
        Assert.Equal("2024-02", living.Month);
        Assert.Equal(living.Summary.LivingCount, living.Counts["living"]);
        Assert.Equal(living.Summary.ExpiredThisMonthCount, living.Counts["expired"]);
        var expired = RegistryPage(await controller.Index(search: prefix, status: RedStarRegistryStatusFilter.Expired, month: "2024-02"));
        Assert.Equal(prefix + "expired", Assert.Single(expired.Rows).PatientCode);
        var alias = RegistryPage(await controller.Index(search: prefix, status: RedStarRegistryStatusFilter.Deceased, month: "2024-02"));
        Assert.Equal(expired.Rows.Select(row => row.EnrollmentId), alias.Rows.Select(row => row.EnrollmentId));
        foreach (var status in new[] { RedStarStatus.AwaitingVerification, RedStarStatus.TransferredOut, RedStarStatus.Discontinued, RedStarStatus.Completed })
        {
            var lifecycle = RegistryPage(await controller.Index(search: prefix, status: Enum.Parse<RedStarRegistryStatusFilter>(status.ToString()), month: "2024-02"));
            Assert.Equal(status, Assert.Single(lifecycle.Rows).Status);
            Assert.Equal(active.Summary, lifecycle.Summary);
        }
        var emptyWorklist = RegistryPage(await controller.Index(search: prefix, worklist: "today", program: RedStarProgram.PrEP, month: "2024-02"));
        Assert.Empty(emptyWorklist.Rows);
        Assert.Equal(active.Summary, emptyWorklist.Summary);
    }

    [Theory]
    [InlineData(2024, 2)]
    [InlineData(2025, 12)]
    [InlineData(2025, 1)]
    public async Task Monthly_expired_selection_uses_inclusive_start_exclusive_end_and_ignores_missing_dates(int year, int month)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var controller = ReportController(db, scope.ServiceProvider);
        var prefix = "BOUNDARY-" + Guid.NewGuid().ToString("N") + "-";
        var start = new DateTime(year, month, 1); var end = start.AddMonths(1);
        var period = start.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var before = RegistryPage(await controller.Index(search: prefix, month: period));
        var dates = new DateTime?[] { start, end.AddDays(-1), start.AddDays(-1), end, null };
        for (var i = 0; i < dates.Length; i++)
            db.RedStarEnrollments.Add(ReportEnrollment(prefix + i, RedStarProgram.ART, RedStarStatus.Deceased, dates[i]));
        await db.SaveChangesAsync();
        var result = RegistryPage(await controller.Index(search: prefix, status: RedStarRegistryStatusFilter.Expired, month: month.ToString(), year: year));
        Assert.Equal(new[] { prefix + "0", prefix + "1" }, result.Rows.Select(row => row.PatientCode));
        Assert.Equal(before.Summary.ExpiredThisMonthCount + 2, result.Summary.ExpiredThisMonthCount);
        Assert.Equal(start, result.Summary.MonthStart);
        var legacy = RegistryPage(await controller.Index(search: prefix, worklist: "expired", month: period));
        Assert.Equal(result.Rows.Select(row => row.EnrollmentId), legacy.Rows.Select(row => row.EnrollmentId));
    }

    [Fact]
    public async Task Report_period_defaults_omitted_components_to_the_clinical_calendar()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var controller = ReportController(db, scope.ServiceProvider); var today = ClinicalClock.Today;
        Assert.Equal(new DateTime(today.Year, today.Month, 1), RegistryPage(await controller.Index(search: null)).Summary.MonthStart);
        Assert.Equal(new DateTime(2024, today.Month, 1), RegistryPage(await controller.Index(search: null, year: 2024)).Summary.MonthStart);
        Assert.Equal(new DateTime(today.Year, 2, 1), RegistryPage(await controller.Index(search: null, month: "2")).Summary.MonthStart);
        Assert.Equal(new DateTime(2024, 2, 1), RegistryPage(await controller.Index(search: null, month: "2024-02", year: 2024)).Summary.MonthStart);
        Assert.Equal(new DateTime(1900, 1, 1), RegistryPage(await controller.Index(search: null, month: "1900-01")).Summary.MonthStart);
        Assert.Equal(new DateTime(2200, 12, 1), RegistryPage(await controller.Index(search: null, month: "2200-12")).Summary.MonthStart);
    }

    [Theory]
    [InlineData("month", "2024-13", null, null)]
    [InlineData("month", "0", null, null)]
    [InlineData("month", "13", null, null)]
    [InlineData("month", "invalid", null, null)]
    [InlineData("month", "2024-02", "year", "2025")]
    [InlineData("year", "1899", null, null)]
    [InlineData("year", "2201", null, null)]
    [InlineData("year", "invalid", null, null)]
    [InlineData("status", "Unknown", null, null)]
    [InlineData("status", "999", null, null)]
    [InlineData("program", "999", null, null)]
    [InlineData("worklist", "Unknown", null, null)]
    public async Task Registry_and_formatted_report_reject_invalid_filters_consistently(string key, string value, string? otherKey, string? otherValue)
    {
        var clinic = await Clinic(); var fields = new Dictionary<string, string> { [key] = value };
        if (otherKey is not null) fields[otherKey] = otherValue!;
        var query = string.Join("&", fields.Select(field => Uri.EscapeDataString(field.Key) + "=" + Uri.EscapeDataString(field.Value)));
        Assert.Equal(HttpStatusCode.BadRequest, (await clinic.GetAsync("/Admin/RedStar?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await clinic.PostFormAsync("/Admin/RedStar/Create", "/Admin/RedStar/Export", fields)).StatusCode);
    }

    [Fact]
    public async Task Dashboard_preserves_period_and_filters_in_pagination_and_download()
    {
        var prefix = "REPORT-UI-" + Guid.NewGuid().ToString("N") + "-";
        await Db(async db =>
        {
            for (var i = 0; i < 26; i++) db.RedStarEnrollments.Add(ReportEnrollment(prefix + i.ToString("D2"), RedStarProgram.ART, RedStarStatus.Active));
            await db.SaveChangesAsync(); return true;
        });
        var html = WebUtility.HtmlDecode(await (await Clinic()).GetStringAsync("/Admin/RedStar?search=" + prefix + "&program=ART&status=Living&month=2024-02"));
        Assert.Contains("Download Formatted Report", html);
        Assert.Contains("action=\"/Admin/RedStar/Export\"", html);
        Assert.Contains("value=\"Living\"", html);
        Assert.Contains("value=\"2024-02\"", html);
        Assert.Contains("page=2", html);
        Assert.Contains("status=Living", html);
        Assert.Contains("month=2024-02", html);
        Assert.Contains("search=" + prefix, html);
        Assert.Contains("program=ART", html);
    }

    private static XDocument WorkbookXml(ZipArchive zip, string path)
    {
        using var stream = Assert.IsType<ZipArchiveEntry>(zip.GetEntry(path)).Open();
        return XDocument.Load(stream);
    }
    private static string CellText(XElement cell) => string.Concat(cell.Descendants().Where(element => element.Name.LocalName is "t" or "v").Select(element => element.Value));
    private static string ReportField(XDocument sheet, string label)
    {
        var cell = Assert.Single(sheet.Descendants(), element => element.Name.LocalName == "c" && CellText(element) == label);
        return CellText(cell.ElementsAfterSelf().First(element => element.Name.LocalName == "c"));
    }

    [Fact]
    public async Task Formatted_report_exports_all_filtered_rows_in_registry_order_with_print_ready_summary()
    {
        var prefix = "XLSX-" + Guid.NewGuid().ToString("N") + "-";
        var cjkCodeName = string.Concat(Enumerable.Repeat("測試", 60));
        var wideCodeName = new string('W', 120);
        await Db(async db =>
        {
            for (var i = 26; i >= 0; i--)
            {
                var enrollment = ReportEnrollment(prefix + i.ToString("D2"), RedStarProgram.ART, RedStarStatus.Active);
                if (i == 0) enrollment.Patient.CodeName = "=1+1";
                if (i == 1) enrollment.Patient.CodeName = cjkCodeName;
                if (i == 2) enrollment.Patient.CodeName = wideCodeName;
                db.RedStarEnrollments.Add(enrollment);
            }
            db.RedStarEnrollments.Add(ReportEnrollment(prefix + "other-program", RedStarProgram.PrEP, RedStarStatus.Active));
            db.RedStarEnrollments.Add(ReportEnrollment(prefix + "expired", RedStarProgram.ART, RedStarStatus.Deceased, new DateTime(2024, 2, 29)));
            await db.SaveChangesAsync(); return true;
        });
        var expected = await Db(async db => new
        {
            ActiveCount = await db.RedStarEnrollments.CountAsync(enrollment => enrollment.Status == RedStarStatus.Active),
            LivingCount = await db.RedStarEnrollments.CountAsync(enrollment => enrollment.Status == RedStarStatus.Active && enrollment.Program == RedStarProgram.ART),
            ExpiredThisMonthCount = await db.RedStarEnrollments.CountAsync(enrollment => enrollment.Status == RedStarStatus.Deceased &&
                enrollment.StatusDate >= new DateTime(2024, 2, 1) && enrollment.StatusDate < new DateTime(2024, 3, 1))
        });
        var clinic = await Clinic();
        var response = await clinic.PostFormAsync("/Admin/RedStar", "/Admin/RedStar/Export", new Dictionary<string, string>
        { ["search"] = prefix, ["program"] = "ART", ["status"] = "Living", ["month"] = "2", ["year"] = "2024", ["page"] = "2" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("RedStar-report-2024-02.xlsx", response.Content.Headers.ContentDisposition?.ToString());
        Assert.True(response.Headers.CacheControl?.NoStore);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var sheet = WorkbookXml(zip, "xl/worksheets/sheet1.xml");
        Assert.Equal(expected.ActiveCount.ToString(CultureInfo.InvariantCulture), ReportField(sheet, "Total Active"));
        Assert.Equal(expected.LivingCount.ToString(CultureInfo.InvariantCulture), ReportField(sheet, "Total Living"));
        Assert.Equal(expected.ExpiredThisMonthCount.ToString(CultureInfo.InvariantCulture), ReportField(sheet, "Total Expired for the Month"));
        Assert.Contains("2024", ReportField(sheet, "Report Period"));
        Assert.Contains(ClinicalClock.Today.ToString("dd MMM yyyy", CultureInfo.InvariantCulture), ReportField(sheet, "Generated Date"));
        Assert.Equal("27", ReportField(sheet, "Matching rows"));
        Assert.Contains(prefix, ReportField(sheet, "Applied filters"));
        var rows = sheet.Descendants().Where(element => element.Name.LocalName == "row").ToList();
        var headers = rows.Single(row => (string?)row.Attribute("r") == "10").Elements().Select(CellText).ToArray();
        Assert.Equal(new[] { "Patient code", "Code name", "Program", "Status", "Next refill", "Follow-up", "Requests" }, headers);
        var data = rows.Where(row => int.Parse((string)row.Attribute("r")!, CultureInfo.InvariantCulture) > 10).ToList();
        Assert.Equal(27, data.Count);
        Assert.Equal(Enumerable.Range(0, 27).Select(i => prefix + i.ToString("D2")), data.Select(row => CellText(row.Elements().First())));
        Assert.Equal(cjkCodeName, CellText(data[1].Elements().ElementAt(1)));
        Assert.Equal(wideCodeName, CellText(data[2].Elements().ElementAt(1)));
        // Native Excel AutoFit needs 158.4pt for this CJK label and 144pt for the wide Latin label.
        Assert.True(double.Parse((string)data[1].Attribute("ht")!, CultureInfo.InvariantCulture) >= 159);
        Assert.True(double.Parse((string)data[2].Attribute("ht")!, CultureInfo.InvariantCulture) >= 144);
        Assert.DoesNotContain(sheet.Descendants(), element => element.Name.LocalName == "f");
        Assert.Contains(sheet.Descendants(), element => element.Name.LocalName == "c" && CellText(element) == "=1+1" && (string?)element.Attribute("t") == "inlineStr");
        Assert.DoesNotContain("PortalUserId", sheet.ToString());
        Assert.DoesNotContain("FullName", sheet.ToString());
        var setup = Assert.Single(sheet.Descendants(), element => element.Name.LocalName == "pageSetup");
        Assert.Equal("landscape", (string?)setup.Attribute("orientation"));
        Assert.Equal("9", (string?)setup.Attribute("paperSize"));
        Assert.Equal("1", (string?)setup.Attribute("fitToWidth"));
        Assert.Equal("0", (string?)setup.Attribute("fitToHeight"));
        Assert.Contains(WorkbookXml(zip, "xl/styles.xml").Descendants(), element => element.Name.LocalName == "xf");
        Assert.Contains(WorkbookXml(zip, "xl/_rels/workbook.xml.rels").Descendants(), element => ((string?)element.Attribute("Type"))?.EndsWith("/styles", StringComparison.Ordinal) == true);
        var names = WorkbookXml(zip, "xl/workbook.xml").Descendants().Where(element => element.Name.LocalName == "definedName").ToList();
        Assert.Contains(names, element => (string?)element.Attribute("name") == "_xlnm.Print_Titles" && element.Value.Contains("$10:$10", StringComparison.Ordinal));
        Assert.Contains(names, element => (string?)element.Attribute("name") == "_xlnm.Print_Area" && element.Value.Contains("$G$37", StringComparison.Ordinal));
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(new MemoryStream(bytes));
        Assert.Equal("Registry", reader.Name);
        var found = new List<string>();
        while (reader.Read()) if (reader.GetValue(0)?.ToString()?.StartsWith(prefix, StringComparison.Ordinal) == true) found.Add(reader.GetValue(0)!.ToString()!);
        Assert.Equal(27, found.Count);
        Assert.True(await Db(db => db.RedStarAudits.AnyAsync(a => a.Action == "FormattedReportExport" && a.Subject == "PrivateRegistry")));
    }

    [Fact]
    public async Task Formatted_expired_report_applies_month_and_empty_report_retains_headers()
    {
        var prefix = "EMPTY-XLSX-" + Guid.NewGuid().ToString("N");
        await Db(async db =>
        {
            db.RedStarEnrollments.Add(ReportEnrollment(prefix + "-inside", RedStarProgram.ART, RedStarStatus.Deceased, new DateTime(2024, 2, 29)));
            db.RedStarEnrollments.Add(ReportEnrollment(prefix + "-outside", RedStarProgram.ART, RedStarStatus.Deceased, new DateTime(2024, 3, 1)));
            await db.SaveChangesAsync(); return true;
        });
        var clinic = await Clinic();
        var legacy = await clinic.GetStringAsync("/Admin/RedStar?search=" + prefix + "&status=5&month=2024-02");
        Assert.Contains(prefix + "-inside", legacy);
        Assert.DoesNotContain(prefix + "-outside", legacy);
        foreach (var filter in new[] { "Expired", "Deceased", "5" })
        {
            var response = await clinic.PostFormAsync("/Admin/RedStar", "/Admin/RedStar/Export", new Dictionary<string, string>
            { ["search"] = prefix, ["status"] = filter, ["month"] = "2024-02" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
            var sheet = WorkbookXml(zip, "xl/worksheets/sheet1.xml");
            Assert.Equal("1", ReportField(sheet, "Matching rows"));
            Assert.Contains(prefix + "-inside", sheet.ToString());
            Assert.DoesNotContain(prefix + "-outside", sheet.ToString());
            Assert.Contains("29 Feb 2024", sheet.ToString());
        }
        var empty = await clinic.PostFormAsync("/Admin/RedStar", "/Admin/RedStar/Export", new Dictionary<string, string>
        { ["search"] = prefix + "-missing", ["status"] = "Expired", ["month"] = "2024-02" });
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        using var emptyZip = new ZipArchive(new MemoryStream(await empty.Content.ReadAsByteArrayAsync()));
        var emptySheet = WorkbookXml(emptyZip, "xl/worksheets/sheet1.xml");
        Assert.Equal("0", ReportField(emptySheet, "Matching rows"));
        Assert.Contains("Patient code", emptySheet.ToString());
        Assert.Contains("Total Living", emptySheet.ToString());
    }

    [Fact]
    public async Task Formatted_report_requires_clinic_access_and_antiforgery_token()
    {
        var clinic = await Clinic();
        Assert.Equal(HttpStatusCode.BadRequest, (await clinic.PostAsync("/Admin/RedStar/Export", new FormUrlEncodedContent([]))).StatusCode);
        var other = await StaffAccounts.PatientServicesAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsync("/Admin/RedStar/Export", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await factory.CreatePortalClient().PostAsync("/Admin/RedStar/Export", new FormUrlEncodedContent([]))).StatusCode);
    }

    [Fact]
    public async Task Formatted_report_audit_failure_blocks_workbook_and_patient_disclosure()
    {
        var item = await Seed(); var interceptor = new RejectFormattedReportAudit();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(interceptor))));
        var clinic = await StaffAccounts.SignInAsync(host, "report-audit." + Guid.NewGuid().ToString("N") + "@fixtures.test", "RedStarStaff");
        var page = "/Admin/RedStar?search=" + Uri.EscapeDataString(item.Code);
        Assert.Contains(item.Code, await clinic.GetStringAsync(page));
        var response = await clinic.PostFormAsync(page, "/Admin/RedStar/Export", new Dictionary<string, string> { ["search"] = item.Code });
        Assert.True(interceptor.Rejected);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Home/ServiceUnavailable", response.Headers.Location?.OriginalString);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.NotEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(item.Code, body);
        Assert.DoesNotContain("Private synthetic patient", body);
    }

    private sealed class RejectFormattedReportAudit : SaveChangesInterceptor
    {
        public bool Rejected { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<RedStarAudit>().Any(entry =>
                entry.State == EntityState.Added && entry.Entity.Action == "FormattedReportExport") == true)
            {
                Rejected = true;
                throw new IOException("Synthetic formatted report audit failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Formatted_report_limit_applies_after_private_search_and_returns_to_the_same_filters()
    {
        var prefix = "LIMIT-XLSX-" + Guid.NewGuid().ToString("N") + "-";
        var patientIds = await Db(async db =>
        {
            var enrollments = Enumerable.Range(0, RedStarRegistryParser.MaxRows + 1)
                .Select(i => ReportEnrollment(prefix + i.ToString("D4"), RedStarProgram.ART, RedStarStatus.Active)).ToArray();
            db.RedStarEnrollments.AddRange(enrollments); await db.SaveChangesAsync();
            return enrollments.Select(enrollment => enrollment.PatientId).ToArray();
        });
        try
        {
            var clinic = await Clinic();
            var fields = new Dictionary<string, string> { ["search"] = prefix, ["status"] = "Living", ["program"] = "ART", ["month"] = "2024-02" };
            var rejected = await clinic.PostFormAsync("/Admin/RedStar/Create", "/Admin/RedStar/Export", fields);
            Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
            var location = WebUtility.UrlDecode(rejected.Headers.Location!.OriginalString);
            Assert.Contains("search=" + prefix, location); Assert.Contains("status=Living", location);
            Assert.Contains("program=ART", location); Assert.Contains("month=2024-02", location);
            Assert.Contains("5,000", await clinic.GetStringAsync(rejected.Headers.Location));
            fields["search"] = prefix + "000";
            var limited = await clinic.PostFormAsync("/Admin/RedStar/Create", "/Admin/RedStar/Export", fields);
            Assert.Equal(HttpStatusCode.OK, limited.StatusCode);
            using var zip = new ZipArchive(new MemoryStream(await limited.Content.ReadAsByteArrayAsync()));
            Assert.Equal("10", ReportField(WorkbookXml(zip, "xl/worksheets/sheet1.xml"), "Matching rows"));
        }
        finally
        {
            await Db(async db =>
            {
                await db.RedStarEnrollments.Where(enrollment => patientIds.Contains(enrollment.PatientId)).ExecuteDeleteAsync();
                await db.RedStarPatients.Where(patient => patientIds.Contains(patient.Id)).ExecuteDeleteAsync();
                return true;
            });
        }
    }

    [Fact]
    public async Task Enrollment_fields_follow_the_supported_living_registry_columns()
    {
        var html = await (await Clinic()).GetStringAsync("/Admin/RedStar/Create");
        var position = -1;
        foreach (var field in new[] { "FullName", "Address", "HospitalNumber", "PatientCode", "CodeName", "EnrolledDate", "StartDate", "ContactNumber", "Medicines", "Status", "Program", "StatusDate", "ReceivingHub" })
        {
            var next = html.IndexOf("name=\"" + field + "\"", StringComparison.Ordinal);
            Assert.True(next > position, field + " must follow the preceding staff registry field.");
            position = next;
        }
        Assert.Contains("appointments and dispensing", html);
    }

    [Fact]
    public async Task Enrollment_offers_all_staff_worksheets_and_refill_dates_remain_separate()
    {
        var html = await (await Clinic()).GetStringAsync("/Admin/RedStar/Create");
        Assert.Contains("name=\"WorksheetLayout\"", html);
        foreach (var sheet in RedStarRegistryLayout.Sheets)
            Assert.Contains("value=\"" + sheet.Name + "\"", html);
        Assert.Contains("data-registry-field=\"DispensedDate\"", html);
        Assert.Contains("data-registry-field=\"NextRefillDate\"", html);
        Assert.DoesNotContain("name=\"DispensedDate\"", html);
        Assert.DoesNotContain("name=\"NextRefillDate\"", html);
        Assert.Contains("/js/redstar-enrollment.js", html);
    }

    [Fact]
    public async Task Invalid_submission_retains_selected_worksheet_and_entered_values()
    {
        var clinic = await Clinic();
        var response = await clinic.PostFormAsync("/Admin/RedStar/Create", "/Admin/RedStar/Create", new Dictionary<string, string>
        {
            ["WorksheetLayout"] = "PrEP", ["FullName"] = "Synthetic layout input", ["Address"] = "Synthetic entered address",
            ["PatientCode"] = "", ["Program"] = "ART", ["Status"] = "AwaitingVerification"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Matches("<option[^>]*value=\"PrEP\"[^>]*selected", html);
        Assert.Contains("value=\"Synthetic layout input\"", html);
        Assert.Contains("value=\"Synthetic entered address\"", html);
        Assert.True(html.IndexOf("name=\"PatientCode\"", StringComparison.Ordinal) < html.IndexOf("name=\"StartDate\"", StringComparison.Ordinal));
        Assert.True(html.IndexOf("name=\"StartDate\"", StringComparison.Ordinal) < html.IndexOf("name=\"CodeName\"", StringComparison.Ordinal));
        Assert.Contains("PrEP Start Date @ RED STAR", html);
    }

    [Theory]
    [InlineData("PrEP")]
    [InlineData("RHIVDA 2025")]
    [InlineData("EXPIRED")]
    public async Task Worksheet_layout_does_not_change_saved_program_status_or_create_schedules(string worksheet)
    {
        var clinic = await Clinic();
        var code = "LAYOUT-" + Guid.NewGuid().ToString("N");
        var response = await clinic.PostFormAsync("/Admin/RedStar/Create", "/Admin/RedStar/Create", new Dictionary<string, string>
        {
            ["WorksheetLayout"] = worksheet, ["PatientCode"] = code, ["FullName"] = "Synthetic presentation choice",
            ["Program"] = "ART", ["Status"] = "AwaitingVerification"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var enrollment = await Db(db => db.RedStarEnrollments.AsNoTracking().Include(enrollment => enrollment.Patient)
            .Include(enrollment => enrollment.Appointments).Include(enrollment => enrollment.Dispenses)
            .SingleAsync(enrollment => enrollment.Patient.PatientCodeBlindIndex == db.Phi.PatientCodeIndex(code)));
        Assert.Equal(RedStarProgram.ART, enrollment.Program);
        Assert.Equal(RedStarStatus.AwaitingVerification, enrollment.Status);
        Assert.Empty(enrollment.Appointments);
        Assert.Empty(enrollment.Dispenses);
    }

    [Theory]
    [InlineData("RHIVDA 2025")]
    [InlineData("REACTIVE WHEN TESTED, NONREACTI")]
    public async Task Worksheet_selection_keeps_unverified_program_activation_blocked(string worksheet)
    {
        var clinic = await Clinic();
        var code = "LAYOUT-BLOCK-" + Guid.NewGuid().ToString("N");
        var response = await clinic.PostFormAsync("/Admin/RedStar/Create", "/Admin/RedStar/Create", new Dictionary<string, string>
        {
            ["WorksheetLayout"] = worksheet, ["PatientCode"] = code, ["FullName"] = "Synthetic unverified layout",
            ["Program"] = "Unverified", ["Status"] = "Active"
        });
        Assert.Contains("Verify the program before activating", await response.Content.ReadAsStringAsync());
        Assert.False(await Db(db => db.RedStarPatients.AnyAsync(patient => patient.PatientCodeBlindIndex == db.Phi.PatientCodeIndex(code))));
    }

    [Fact]
    public async Task Unknown_worksheet_is_rejected_and_edit_defaults_to_the_saved_program()
    {
        var clinic = await Clinic();
        var response = await clinic.PostFormAsync("/Admin/RedStar/Create", "/Admin/RedStar/Create", new Dictionary<string, string>
        {
            ["WorksheetLayout"] = "Unknown worksheet", ["PatientCode"] = "INVALID-LAYOUT", ["FullName"] = "Synthetic invalid layout",
            ["Program"] = "ART", ["Status"] = "AwaitingVerification"
        });
        Assert.Contains("Select a supported worksheet layout", await response.Content.ReadAsStringAsync());
        Assert.False(await Db(db => db.RedStarPatients.AnyAsync(patient => patient.PatientCodeBlindIndex == db.Phi.PatientCodeIndex("INVALID-LAYOUT"))));
        var item = await Seed(RedStarProgram.PrEP);
        var html = await clinic.GetStringAsync("/Admin/RedStar/Edit/" + item.EnrollmentId);
        Assert.Matches("<option[^>]*value=\"PrEP\"[^>]*selected", html);
        Assert.Contains("PrEP Start Date @ RED STAR", html);
    }
    private async Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> operation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await operation(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
    private async Task<Fixture> Seed(RedStarProgram program = RedStarProgram.ART)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = "redstar.patient." + suffix + "@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, email, false);
        return await Db(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email);
            var patient = new RedStarPatient { PatientCode = "OPS-" + suffix, FullName = "Private synthetic patient " + suffix,
                CodeName = "Synthetic code name", PortalUserId = user.Id, EmailRemindersEnabled = true };
            var enrollment = new RedStarEnrollment { Patient = patient, Program = program, Status = RedStarStatus.Active, Medicines = "Synthetic treatment" };
            var appointment = new RedStarAppointment { Enrollment = enrollment, Kind = RedStarAppointmentKind.Refill,
                DueDate = ClinicalClock.Today.AddDays(7), RemindersVerified = true };
            db.RedStarAppointments.Add(appointment);
            await db.SaveChangesAsync();
            db.RedStarReminders.Add(new RedStarReminder { AppointmentId = appointment.Id, AppointmentRevision = 1, Stage = "SevenDaysBefore",
                Channel = RedStarReminderChannel.Email, PortalUserId = user.Id, ProtectedPayload = [1, 2, 3], CreatedUtc = DateTimeOffset.UtcNow });
            db.RedStarRescheduleRequests.Add(new RedStarRescheduleRequest { AppointmentId = appointment.Id, AppointmentRevision = 1,
                RequestedDate = ClinicalClock.Today.AddDays(9), CreatedUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return new Fixture(patient.Id, enrollment.Id, appointment.Id, user.Id, email, patient.PatientCode);
        });
    }
    private async Task<Dictionary<string, string>> DispenseFields(Fixture fixture, DateTime? next = null)
    {
        return await Db(async db =>
        {
            var appointment = await db.RedStarAppointments.Include(a => a.Enrollment).SingleAsync(a => a.Id == fixture.AppointmentId);
            var fields = new Dictionary<string, string>
            {
                ["AppointmentVersion"] = Convert.ToBase64String(appointment.RowVersion), ["EnrollmentVersion"] = Convert.ToBase64String(appointment.Enrollment.RowVersion),
                ["DispensedDate"] = ClinicalClock.Today.ToString("yyyy-MM-dd"), ["DaysSupplied"] = "30", ["Medicines"] = "Synthetic dispensed medicine"
            };
            if (next.HasValue) { fields["NextRefillDate"] = next.Value.ToString("yyyy-MM-dd"); fields["NextDateVerified"] = "true"; }
            return fields;
        });
    }

    [Fact]
    public async Task Enrollment_creation_is_private_and_portal_link_requires_identity_verification()
    {
        var suffix = Guid.NewGuid().ToString("N"); var code = "CREATE-" + suffix;
        var clinic = await Clinic();
        var created = await clinic.PostFormAsync("/Admin/RedStar/Create", "/Admin/RedStar/Create", new Dictionary<string, string>
        {
            ["PatientCode"] = code, ["CodeName"] = "Synthetic code", ["FullName"] = "Private synthetic creation " + suffix,
            ["Program"] = "ART", ["Status"] = "Active", ["EnrolledDate"] = ClinicalClock.Today.ToString("yyyy-MM-dd"), ["Medicines"] = "Synthetic medicine"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var enrollment = await Db(db => db.RedStarEnrollments.AsNoTracking().Include(e => e.Patient).SingleAsync(e => e.Patient.PatientCodeBlindIndex == db.Phi.PatientCodeIndex(code)));
        Assert.Equal(RedStarStatus.Active, enrollment.Status);
        Assert.False(await Db(db => db.PatientRecords.AnyAsync(p => p.FullName == enrollment.Patient.FullName)));
        var email = "redstar.link." + suffix + "@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, email, false);
        var fields = new Dictionary<string, string> { ["Email"] = email, ["PatientVersion"] = Convert.ToBase64String(enrollment.Patient.RowVersion) };
        await clinic.PostFormAsync("/Admin/RedStar/Details/" + enrollment.Id, "/Admin/RedStar/Link/" + enrollment.PatientId, fields);
        Assert.Null(await Db(db => db.RedStarPatients.Where(p => p.Id == enrollment.PatientId).Select(p => p.PortalUserId).SingleAsync()));
        fields["IdentityVerified"] = "true";
        Assert.Equal(HttpStatusCode.Redirect, (await clinic.PostFormAsync("/Admin/RedStar/Details/" + enrollment.Id, "/Admin/RedStar/Link/" + enrollment.PatientId, fields)).StatusCode);
        var linked = await Db(db => db.RedStarPatients.AsNoTracking().SingleAsync(p => p.Id == enrollment.PatientId));
        Assert.Equal(await Db(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync()), linked.PortalUserId);
        Assert.False(linked.EmailRemindersEnabled);
        Assert.True(await Db(db => db.RedStarAudits.AnyAsync(a => a.Subject == "Patient/" + linked.Id && a.Action == "Verify portal link")));
        Assert.False(await Db(db => db.AdminAuditLogs.AnyAsync(a => a.Entity == "RedStar")));
    }

    [Fact]
    public async Task Unlink_cancels_pending_delivery_requests_and_old_account_binding()
    {
        var item = await Seed(); var clinic = await Clinic();
        var version = await Db(async db => Convert.ToBase64String(await db.RedStarPatients.Where(p => p.Id == item.PatientId).Select(p => p.RowVersion).SingleAsync()));
        var response = await clinic.PostFormAsync("/Admin/RedStar/Details/" + item.EnrollmentId, "/Admin/RedStar/Unlink/" + item.PatientId,
            new Dictionary<string, string> { ["PatientVersion"] = version, ["IdentityVerified"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var patient = await Db(db => db.RedStarPatients.AsNoTracking().SingleAsync(p => p.Id == item.PatientId));
        Assert.Null(patient.PortalUserId); Assert.False(patient.EmailRemindersEnabled);
        var appointment = await Db(db => db.RedStarAppointments.AsNoTracking().SingleAsync(a => a.Id == item.AppointmentId));
        Assert.Equal(2, appointment.Revision); Assert.Equal(RedStarAppointmentState.Scheduled, appointment.State);
        var delivery = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r => r.AppointmentId == item.AppointmentId));
        Assert.Equal(RedStarDeliveryState.Cancelled, delivery.State); Assert.Null(delivery.ProtectedPayload);
        Assert.Equal(RedStarRequestState.Declined, await Db(db => db.RedStarRescheduleRequests.Where(r => r.AppointmentId == item.AppointmentId).Select(r => r.State).SingleAsync()));
    }

    [Fact]
    public async Task Dispensing_is_unique_and_supply_estimate_does_not_choose_the_next_visit()
    {
        var item = await Seed(); var clinic = await Clinic(); var next = ClinicalClock.Today.AddDays(14);
        var fields = await DispenseFields(item, next);
        var response = await clinic.PostFormAsync("/Admin/RedStar/Details/" + item.EnrollmentId, "/Admin/RedStar/Dispense/" + item.AppointmentId, fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var dispense = await Db(db => db.RedStarDispenses.AsNoTracking().SingleAsync(d => d.AppointmentId == item.AppointmentId));
        Assert.Equal(30, dispense.DaysSupplied); Assert.Equal(ClinicalClock.Today.AddDays(30), dispense.EstimatedSupplyEnd);
        Assert.Equal("Synthetic dispensed medicine", await Db(db => db.RedStarEnrollments.Where(e => e.Id == item.EnrollmentId).Select(e => e.Medicines).SingleAsync()));
        var nextAppointment = await Db(db => db.RedStarAppointments.AsNoTracking().SingleAsync(a => a.EnrollmentId == item.EnrollmentId && a.State == RedStarAppointmentState.Scheduled));
        Assert.Equal(next, nextAppointment.DueDate); Assert.True(nextAppointment.RemindersVerified);
        Assert.Equal(RedStarAppointmentState.PickedUp, await Db(db => db.RedStarAppointments.Where(a => a.Id == item.AppointmentId).Select(a => a.State).SingleAsync()));
        Assert.Equal(RedStarDeliveryState.Cancelled, await Db(db => db.RedStarReminders.Where(r => r.AppointmentId == item.AppointmentId).Select(r => r.State).SingleAsync()));
        await clinic.PostFormAsync("/Admin/RedStar/Details/" + item.EnrollmentId, "/Admin/RedStar/Dispense/" + item.AppointmentId, fields);
        Assert.Equal(1, await Db(db => db.RedStarDispenses.CountAsync(d => d.AppointmentId == item.AppointmentId)));
        Assert.Equal(2, await Db(db => db.RedStarAppointments.CountAsync(a => a.EnrollmentId == item.EnrollmentId)));
        Assert.Contains("This record changed", await clinic.GetStringAsync("/Admin/RedStar/Details/" + item.EnrollmentId));
    }

    [Theory]
    [InlineData(RedStarProgram.PEP)]
    [InlineData(RedStarProgram.NewbornProphylaxis)]
    public async Task One_course_dispensing_never_creates_an_automatic_refill(RedStarProgram program)
    {
        var item = await Seed(program); var clinic = await Clinic();
        Assert.Equal(HttpStatusCode.Redirect, (await clinic.PostFormAsync("/Admin/RedStar/Details/" + item.EnrollmentId,
            "/Admin/RedStar/Dispense/" + item.AppointmentId, await DispenseFields(item))).StatusCode);
        Assert.False(await Db(db => db.RedStarAppointments.AnyAsync(a => a.EnrollmentId == item.EnrollmentId && a.State == RedStarAppointmentState.Scheduled)));
    }

    [Fact]
    public async Task Concurrent_pickups_commit_a_single_history_record()
    {
        var item = await Seed(); var clinic = await Clinic(); var fields = await DispenseFields(item);
        fields["__RequestVerificationToken"] = StaffAccounts.Token(await clinic.GetStringAsync("/Admin/RedStar/Details/" + item.EnrollmentId));
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => clinic.PostAsync("/Admin/RedStar/Dispense/" + item.AppointmentId, new FormUrlEncodedContent(fields))));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Redirect, response.StatusCode));
        Assert.Equal(1, await Db(db => db.RedStarDispenses.CountAsync(d => d.AppointmentId == item.AppointmentId)));
    }

    [Fact]
    public async Task Staff_can_verify_an_existing_overdue_date_without_moving_it()
    {
        var item = await Seed(); var clinic = await Clinic(); var overdue = ClinicalClock.Today.AddDays(-14);
        await Db(async db => { var appointment = await db.RedStarAppointments.SingleAsync(a => a.Id == item.AppointmentId);
            appointment.DueDate = overdue; appointment.RemindersVerified = false; await db.SaveChangesAsync(); return true; });
        var fields = await Db(async db =>
        {
            var appointment = await db.RedStarAppointments.Include(a => a.Enrollment).SingleAsync(a => a.Id == item.AppointmentId);
            return new Dictionary<string, string> { ["AppointmentVersion"] = Convert.ToBase64String(appointment.RowVersion),
                ["EnrollmentVersion"] = Convert.ToBase64String(appointment.Enrollment.RowVersion), ["Kind"] = "Refill",
                ["DueDate"] = overdue.ToString("yyyy-MM-dd"), ["RemindersVerified"] = "true" };
        });
        Assert.Equal(HttpStatusCode.Redirect, (await clinic.PostFormAsync("/Admin/RedStar/Details/" + item.EnrollmentId,
            "/Admin/RedStar/UpdateAppointment/" + item.AppointmentId, fields)).StatusCode);
        var verified = await Db(db => db.RedStarAppointments.AsNoTracking().SingleAsync(a => a.Id == item.AppointmentId));
        Assert.Equal(overdue, verified.DueDate); Assert.True(verified.RemindersVerified); Assert.Equal(2, verified.Revision);
        fields["AppointmentVersion"] = Convert.ToBase64String(verified.RowVersion); fields["DueDate"] = overdue.AddDays(1).ToString("yyyy-MM-dd");
        await clinic.PostFormAsync("/Admin/RedStar/Details/" + item.EnrollmentId, "/Admin/RedStar/UpdateAppointment/" + item.AppointmentId, fields);
        Assert.Equal(overdue, await Db(db => db.RedStarAppointments.Where(a => a.Id == item.AppointmentId).Select(a => a.DueDate).SingleAsync()));
    }

    [Fact]
    public async Task Closing_enrollment_cancels_schedules_and_reminders_in_one_transaction()
    {
        var item = await Seed(); var clinic = await Clinic();
        var enrollment = await Db(db => db.RedStarEnrollments.AsNoTracking().Include(e => e.Patient).SingleAsync(e => e.Id == item.EnrollmentId));
        var response = await clinic.PostFormAsync("/Admin/RedStar/Edit/" + item.EnrollmentId, "/Admin/RedStar/Edit/" + item.EnrollmentId,
            new Dictionary<string, string>
            {
                ["PatientCode"] = item.Code, ["CodeName"] = enrollment.Patient.CodeName, ["FullName"] = enrollment.Patient.FullName,
                ["Program"] = "ART", ["Status"] = "TransferredOut", ["StatusDate"] = ClinicalClock.Today.ToString("yyyy-MM-dd"),
                ["ReceivingHub"] = "Synthetic receiving clinic", ["Medicines"] = enrollment.Medicines,
                ["PatientVersion"] = Convert.ToBase64String(enrollment.Patient.RowVersion), ["EnrollmentVersion"] = Convert.ToBase64String(enrollment.RowVersion)
            });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(RedStarStatus.TransferredOut, await Db(db => db.RedStarEnrollments.Where(e => e.Id == item.EnrollmentId).Select(e => e.Status).SingleAsync()));
        Assert.Equal(RedStarAppointmentState.Cancelled, await Db(db => db.RedStarAppointments.Where(a => a.Id == item.AppointmentId).Select(a => a.State).SingleAsync()));
        Assert.Equal(RedStarRequestState.Declined, await Db(db => db.RedStarRescheduleRequests.Where(r => r.AppointmentId == item.AppointmentId).Select(r => r.State).SingleAsync()));
        var reminder = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r => r.AppointmentId == item.AppointmentId));
        Assert.Equal(RedStarDeliveryState.Cancelled, reminder.State); Assert.Null(reminder.ProtectedPayload);
    }

    [Fact]
    public async Task Live_role_revocation_and_lockout_deny_clinic_access()
    {
        var email = "redstar.revoked." + Guid.NewGuid().ToString("N") + "@fixtures.test";
        var client = await StaffAccounts.SignInAsync(factory, email, "RedStarStaff");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/RedStar")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await users.FindByEmailAsync(email))!;
        Assert.True((await users.RemoveFromRoleAsync(user, "RedStarStaff")).Succeeded);
        var denied = await client.GetAsync("/Admin/RedStar");
        Assert.True(denied.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
        Assert.True((await users.AddToRoleAsync(user, "RedStarStaff")).Succeeded);
        await users.SetLockoutEnabledAsync(user, true); await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddDays(1));
        denied = await client.GetAsync("/Admin/RedStar"); Assert.True(denied.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
    }
}
