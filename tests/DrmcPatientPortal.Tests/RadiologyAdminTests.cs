using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed class RadiologyAdminTests(PortalFactory factory)
{
    private async Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private Task<int> PrimaryPatientIdAsync() =>
        Db(async db =>
        {
            var patient = await db.PatientRecords.SingleAsync(p => p.PortalUser!.Email == PortalFactory.PrimaryEmail);
            if (!patient.HasHospitalNumber)
            {
                patient.HospitalNumber = "RAD-FIXTURE-PRIMARY";
                await db.SaveChangesAsync();
            }
            return patient.Id;
        });

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\nSynthetic radiology report\n%%EOF");

    private static Dictionary<string, string> Study(string status = "2", string? rowVersion = null)
    {
        var form = new Dictionary<string, string>
        {
            ["Modality"] = "0", ["Status"] = status,
            ["PerformedAt"] = "2026-09-01", ["ReleasedAt"] = "2026-09-01T15:00",
        };
        if (rowVersion is not null) form["RowVersion"] = rowVersion;
        return form;
    }

    private static async Task<HttpResponseMessage> SavePdf(HttpClient client, string path, Dictionary<string, string> fields,
        byte[]? bytes, string fileName = "report.pdf", string contentType = "application/pdf")
    {
        var page = await client.GetStringAsync(path);
        Assert.Contains("enctype=\"multipart/form-data\"", page);
        using var body = new MultipartFormDataContent();
        foreach (var (key, value) in fields) body.Add(new StringContent(value), key);
        body.Add(new StringContent(StaffAccounts.Token(page)), "__RequestVerificationToken");
        if (bytes is not null)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            body.Add(file, "Report", fileName);
        }
        return await client.PostAsync(path, body);
    }

    [Fact]
    public async Task Fixtures_seed_radiology_studies_across_modalities_and_statuses()
    {
        var patient = await PrimaryPatientIdAsync();
        var studies = await Db(db => db.RadiologyStudies.Where(r => r.PatientRecordId == patient).ToListAsync());
        Assert.True(studies.Select(s => s.Modality).Distinct().Count() >= 3);
        Assert.True(studies.Select(s => s.Status).Distinct().Count() >= 3);
        var empty = await Db(db => db.RadiologyStudies.CountAsync(r => r.Patient.PortalUser!.Email == PortalFactory.EmptyEmail));
        Assert.Equal(0, empty);
    }

    [Fact]
    public async Task Admin_can_create_edit_and_delete_a_study_and_stale_edits_are_rejected()
    {
        var client = await StaffAccounts.AdminAsync(factory);
        var patient = await PrimaryPatientIdAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/RadiologyStudies")).StatusCode);
        Assert.Contains($"/Admin/RadiologyStudies/Create/{patient}", await client.GetStringAsync("/Admin/RadiologyStudies/Create"));

        var invalid = await client.PostFormAsync($"/Admin/RadiologyStudies/Create/{patient}", $"/Admin/RadiologyStudies/Create/{patient}", Study());
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Choose the radiology report PDF.", await invalid.Content.ReadAsStringAsync());

        var before = await Db(db => db.RadiologyStudies.MaxAsync(r => r.Id));
        var created = await SavePdf(client, $"/Admin/RadiologyStudies/Create/{patient}", Study(), Pdf);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var study = await Db(db => db.RadiologyStudies.SingleAsync(r => r.Id > before));
        Assert.Equal(patient, study.PatientRecordId);
        Assert.StartsWith("PORTAL-RAD-", study.AccessionNumber);
        Assert.Equal("Radiology report", study.StudyName);
        Assert.Empty(study.OrderingPhysician);
        Assert.Empty(study.RadiologistName);
        Assert.Empty(study.Findings);
        Assert.NotNull(study.ReportFileName);

        var editPage = await client.GetStringAsync($"/Admin/RadiologyStudies/Edit/{study.Id}");
        Assert.Contains(">XRY</option>", editPage);
        Assert.Contains("type=\"date\"", editPage);
        foreach (var field in new[] { "AccessionNumber", "StudyName", "BodyRegion", "HistoricalDoctorName", "Findings", "Impression", "ClinicalEncounterId" })
            Assert.DoesNotContain($"name=\"{field}\"", editPage);
        var version = System.Text.RegularExpressions.Regex.Match(editPage, "name=\"RowVersion\" value=\"([^\"]+)\"").Groups[1].Value;
        version = WebUtility.HtmlDecode(version);
        var fields = Study(rowVersion: version);
        fields["Modality"] = "CT";
        var edited = await SavePdf(client, $"/Admin/RadiologyStudies/Edit/{study.Id}", fields, null);
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.Equal(study.ReportFileName, await Db(db => db.RadiologyStudies.Where(r => r.Id == study.Id).Select(r => r.ReportFileName).SingleAsync()));
        var stale = await client.PostFormAsync($"/Admin/RadiologyStudies/Edit/{study.Id}", $"/Admin/RadiologyStudies/Edit/{study.Id}", Study(rowVersion: version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var audit = await Db(db => db.AdminAuditLogs.Where(a => a.Entity == "RadiologyStudies" && a.RecordKey == study.Id.ToString()).ToListAsync());
        Assert.Contains(audit, a => a.Action == "Create");
        Assert.Contains(audit, a => a.Action == "Edit" && a.ChangedFields.Contains("Modality"));
        Assert.All(audit, a => { Assert.Null(a.OldValues); Assert.Null(a.NewValues); });

        var deletePage = await client.GetStringAsync($"/Admin/RadiologyStudies/Delete/{study.Id}");
        var current = WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(deletePage, "name=\"rowVersion\" value=\"([^\"]+)\"").Groups[1].Value);
        var deleted = await client.PostFormAsync($"/Admin/RadiologyStudies/Delete/{study.Id}", $"/Admin/RadiologyStudies/Delete/{study.Id}", new Dictionary<string, string> { ["rowVersion"] = current });
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.False(await Db(db => db.RadiologyStudies.AnyAsync(r => r.Id == study.Id)));
    }

    [Fact]
    public async Task Patient_deletion_is_blocked_while_radiology_studies_exist()
    {
        var client = await StaffAccounts.AdminAsync(factory);
        var patient = await Db(async db =>
        {
            var record = new PatientRecord { FirstName = "Synthetic", LastName = "Deletion Blocked" };
            db.PatientRecords.Add(record);
            await db.SaveChangesAsync();
            db.RadiologyStudies.Add(new RadiologyStudy
            {
                PatientRecordId = record.Id, AccessionNumber = "DRMC-RAD-TEST-BLOCK", StudyName = "Synthetic", BodyRegion = "Chest",
                PerformedAt = new DateTime(2026, 9, 1), Status = RadiologyStatus.InProgress,
            });
            await db.SaveChangesAsync();
            return record.Id;
        });
        var deletePage = await client.GetStringAsync($"/Admin/Patients/Delete/{patient}");
        Assert.Contains("Radiology studies", deletePage);
        var version = WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(deletePage, "name=\"rowVersion\" value=\"([^\"]+)\"").Groups[1].Value);
        var response = await client.PostFormAsync($"/Admin/Patients/Delete/{patient}", $"/Admin/Patients/Delete/{patient}", new Dictionary<string, string> { ["rowVersion"] = version });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Radiology studies: 1", await client.GetStringAsync(response.Headers.Location!.OriginalString));
        Assert.True(await Db(db => db.PatientRecords.AnyAsync(p => p.Id == patient)));
    }
}
