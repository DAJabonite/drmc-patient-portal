using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed class LabReportTests(PortalFactory factory)
{
    private WebApplicationFactory<Program>? enabledHost;
    private WebApplicationFactory<Program> Enabled => enabledHost ??=
        factory.WithWebHostBuilder(b => b.UseSetting("PatientResults:ShowFullResults", "true"));
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\nSynthetic lab report\n%%EOF");

    private async Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private Task<int> PatientId() => Db(db => db.PatientRecords
        .Where(p => p.PortalUser!.Email == PortalFactory.PrimaryEmail).Select(p => p.Id).SingleAsync());

    private static Dictionary<string, string> Fields(string accession, string? version = null) => new()
    {
        ["AccessionNumber"] = accession, ["TestName"] = "Synthetic PDF laboratory result",
        ["Category"] = "Hematology", ["Status"] = "Available",
        ["CollectedAt"] = PatientResultsDisclosure.ManilaNow.AddDays(-2).ToString("yyyy-MM-ddTHH:mm:ss"),
        ["ReleasedAt"] = PatientResultsDisclosure.ManilaNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss"),
        ["PhysicianSource"] = "HistoricalName", ["HistoricalDoctorName"] = "Synthetic ordering physician",
        ["Pathologist.PhysicianSource"] = "HistoricalName", ["Pathologist.HistoricalDoctorName"] = "Synthetic pathologist",
        ["RowVersion"] = version ?? ""
    };

    private static async Task<HttpResponseMessage> Save(HttpClient admin, string path, Dictionary<string, string> fields,
        byte[]? pdf, string fileName = "report.pdf", string contentType = "application/pdf")
    {
        var page = await admin.GetStringAsync(path);
        Assert.Contains("enctype=\"multipart/form-data\"", page);
        Assert.Contains("name=\"Report\"", page);
        using var body = new MultipartFormDataContent();
        foreach (var (key, value) in fields) body.Add(new StringContent(value), key);
        body.Add(new StringContent(StaffAccounts.Token(page)), "__RequestVerificationToken");
        if (pdf is not null)
        {
            var file = new ByteArrayContent(pdf);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            body.Add(file, "Report", fileName);
        }
        return await admin.PostAsync(path, body);
    }

    private async Task<LabResult> CreateReport()
    {
        var accession = "PDF-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var admin = await StaffAccounts.AdminAsync(factory);
        var response = await Save(admin, $"/Admin/LabResults/Create/{await PatientId()}", Fields(accession), Pdf);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return await Db(db => db.LabResults.AsNoTracking().SingleAsync(l => l.AccessionNumber == accession));
    }

    private Task<string> Version(int id) => Db(async db => Convert.ToBase64String(
        (byte[])db.Entry(await db.LabResults.SingleAsync(l => l.Id == id)).Property("RowVersion").CurrentValue!));

    private static async Task<HttpClient> Patient(WebApplicationFactory<Program> host, string email)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await client.SignInSuccessfullyAsync(email, PortalFactory.Password);
        return client;
    }

    [Theory]
    [InlineData(true, true, "Available", -1, true)]
    [InlineData(false, true, "Available", -1, false)]
    [InlineData(true, false, "Available", -1, false)]
    [InlineData(true, true, "Pending Verification", -1, false)]
    [InlineData(true, true, "Available", 1, false)]
    [InlineData(true, true, "Available", null, false)]
    public async Task Patient_action_matches_actual_pdf_eligibility(bool disclosure, bool attached,
        string status, int? releaseDays, bool available)
    {
        var patientId = await PatientId();
        var id = await Db(async db =>
        {
            var lab = new LabResult
            {
                PatientRecordId = patientId, AccessionNumber = "ELIG-" + Guid.NewGuid().ToString("N"),
                TestName = "Synthetic eligibility result", CollectedAt = PatientResultsDisclosure.ManilaNow.AddDays(-2),
                Status = status, ReleasedAt = releaseDays.HasValue ? PatientResultsDisclosure.ManilaNow.AddDays(releaseDays.Value) : null,
                ReportFileName = attached ? Guid.NewGuid().ToString("N") + ".pdf" : null
            };
            db.LabResults.Add(lab); await db.SaveChangesAsync(); return lab.Id;
        });
        var patient = await Patient(disclosure ? Enabled : factory, PortalFactory.PrimaryEmail);
        var index = await patient.GetStringAsync("/Patient/LabResults");
        var action = Regex.Match(index, $"<a(?=[^>]*href=\"/Patient/LabResults/Details/{id}\")[^>]*>[\\s\\S]*?</a>").Value;
        Assert.NotEmpty(action);
        Assert.Contains(available ? "Report available" : "Report unavailable", action);
        Assert.Contains(available ? "btn-success" : "btn-outline-secondary", action);
        Assert.DoesNotContain("Check Status &amp; Location", index);
        Assert.DoesNotContain("Claiming Unit:", index);
        var detail = await patient.GetStringAsync($"/Patient/LabResults/Details/{id}");
        Assert.Contains("Official Lab Report (PDF)", detail);
        Assert.DoesNotContain("Claiming Requirements", detail);
        Assert.DoesNotContain("Releasing Location", detail);
        Assert.Equal(available, detail.Contains("name=\"password\""));
    }

    [Fact]
    public async Task Create_edit_and_blank_upload_preserve_encryption_and_audit()
    {
        var lab = await CreateReport();
        Assert.NotNull(lab.ReportFileName);
        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<ILabReportStorage>();
        Assert.Equal(Pdf, await storage.ReadAsync(lab.Id, lab.ReportFileName!, default));
        var reportRoot = scope.ServiceProvider.GetRequiredService<IOptions<LabReportStorageOptions>>().Value.RootPath;
        var encrypted = await File.ReadAllBytesAsync(Path.Combine(reportRoot, lab.ReportFileName!));
        Assert.False(encrypted.AsSpan().StartsWith("%PDF-"u8));
        var admin = await StaffAccounts.AdminAsync(factory);
        var replacement = Encoding.ASCII.GetBytes("%PDF-1.4\nReplacement synthetic report\n%%EOF");
        var response = await Save(admin, $"/Admin/LabResults/Edit/{lab.Id}", Fields(lab.AccessionNumber, await Version(lab.Id)), replacement);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var updated = await Db(db => db.LabResults.AsNoTracking().SingleAsync(l => l.Id == lab.Id));
        Assert.NotEqual(lab.ReportFileName, updated.ReportFileName);
        Assert.Equal(replacement, await storage.ReadAsync(lab.Id, updated.ReportFileName!, default));
        await Assert.ThrowsAnyAsync<IOException>(() => storage.ReadAsync(lab.Id, lab.ReportFileName!, default));
        response = await Save(admin, $"/Admin/LabResults/Edit/{lab.Id}", Fields(lab.AccessionNumber, await Version(lab.Id)), null);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var retained = await Db(db => db.LabResults.AsNoTracking().SingleAsync(l => l.Id == lab.Id));
        Assert.Equal(updated.ReportFileName, retained.ReportFileName);
        Assert.Equal(updated.ReportUploadedAtUtc, retained.ReportUploadedAtUtc);
        Assert.True(await Db(db => db.AdminAuditLogs.AnyAsync(a => a.Entity == "LabResults" && a.RecordKey == lab.Id.ToString()
            && a.Action == "Edit" && a.ChangedFields.Contains("ReportFileName"))));
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("extension")]
    [InlineData("size")]
    public async Task Invalid_pdf_rolls_back_record_edits_and_keeps_current_report(string invalid)
    {
        var lab = await CreateReport();
        var admin = await StaffAccounts.AdminAsync(factory);
        var fields = Fields(lab.AccessionNumber, await Version(lab.Id));
        fields["TestName"] = "This rejected edit must not persist";
        var bytes = invalid == "signature" ? Encoding.ASCII.GetBytes("not a PDF")
            : invalid == "size" ? new byte[LabReportStorage.MaximumBytes + 1] : Pdf;
        var response = await Save(admin, $"/Admin/LabResults/Edit/{lab.Id}", fields, bytes, invalid == "extension" ? "report.txt" : "report.pdf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var current = await Db(db => db.LabResults.AsNoTracking().SingleAsync(l => l.Id == lab.Id));
        Assert.Equal(lab.TestName, current.TestName);
        Assert.Equal(lab.ReportFileName, current.ReportFileName);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(Pdf, await scope.ServiceProvider.GetRequiredService<ILabReportStorage>().ReadAsync(lab.Id, lab.ReportFileName!, default));
    }

    [Fact]
    public async Task Invalid_pdf_does_not_create_a_partial_lab_record()
    {
        var accession = "REJECT-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var admin = await StaffAccounts.AdminAsync(factory);
        var response = await Save(admin, $"/Admin/LabResults/Create/{await PatientId()}", Fields(accession), Encoding.ASCII.GetBytes("not a PDF"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await Db(db => db.LabResults.AnyAsync(l => l.AccessionNumber == accession)));
    }

    [Fact]
    public async Task Pdf_requires_owner_password_and_antiforgery_for_each_action()
    {
        var lab = await CreateReport();
        var owner = await Patient(Enabled, PortalFactory.PrimaryEmail);
        var details = $"/Patient/LabResults/Details/{lab.Id}";
        var report = $"/Patient/LabResults/Report/{lab.Id}";
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync(report, new FormUrlEncodedContent(
            new Dictionary<string, string> { ["password"] = PortalFactory.Password, ["intent"] = "view" }))).StatusCode);
        foreach (var password in new[] { "", "Incorrect#Password" })
        {
            var failed = await owner.PostFormAsync(details, report, new Dictionary<string, string> { ["password"] = password, ["intent"] = "view" });
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
            Assert.Equal("text/html", failed.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain("Incorrect#Password", await failed.Content.ReadAsStringAsync());
        }
        foreach (var intent in new[] { "view", "download" })
        {
            var response = await owner.PostFormAsync(details, report, new Dictionary<string, string> { ["password"] = PortalFactory.Password, ["intent"] = intent });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Pdf, await response.Content.ReadAsByteArrayAsync());
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Contains(intent == "view" ? "inline" : "attachment", response.Content.Headers.ContentDisposition!.ToString());
        }
        var other = await Patient(Enabled, PortalFactory.EmptyEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(details)).StatusCode);
        var denied = await other.PostFormAsync("/Identity/Account/Login", report,
            new Dictionary<string, string> { ["password"] = PortalFactory.Password, ["intent"] = "view" });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.True(await Db(db => db.AuditLogs.AnyAsync(a => a.Action == "VIEW_LAB_PDF" && a.Resource == $"LabResult/{lab.Id}")));
        Assert.True(await Db(db => db.AuditLogs.AnyAsync(a => a.Action == "DOWNLOAD_LAB_PDF" && a.Resource == $"LabResult/{lab.Id}")));
    }
}
