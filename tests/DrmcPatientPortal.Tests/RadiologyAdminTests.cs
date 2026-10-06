using System.Net;
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
        Db(db => db.PatientRecords.Where(p => p.PortalUser!.Email == PortalFactory.PrimaryEmail).Select(p => p.Id).SingleAsync());

    private static Dictionary<string, string> Study(string accession, string status = "2", string? rowVersion = null, string findings = "Synthetic findings.")
    {
        var form = new Dictionary<string, string>
        {
            ["AccessionNumber"] = accession, ["StudyName"] = "Synthetic chest X-ray", ["Modality"] = "0", ["BodyRegion"] = "Chest",
            ["Status"] = status, ["PerformedAt"] = "2026-09-01T09:00", ["ReleasedAt"] = "2026-09-01T15:00",
            ["PhysicianSource"] = "1", ["HistoricalDoctorName"] = "Dr. Synthetic Orderer",
            ["Radiologist.PhysicianSource"] = "1", ["Radiologist.HistoricalDoctorName"] = "Dr. Synthetic Reader",
            ["Findings"] = findings, ["Impression"] = "Synthetic impression.",
        };
        if (rowVersion is not null) form["RowVersion"] = rowVersion;
        return form;
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

        var invalid = await client.PostFormAsync($"/Admin/RadiologyStudies/Create/{patient}", $"/Admin/RadiologyStudies/Create/{patient}", Study("drmc-rad-test-0001", findings: ""));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("A final or amended report needs findings.", await invalid.Content.ReadAsStringAsync());

        var created = await client.PostFormAsync($"/Admin/RadiologyStudies/Create/{patient}", $"/Admin/RadiologyStudies/Create/{patient}", Study("drmc-rad-test-0001"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var study = await Db(db => db.RadiologyStudies.SingleAsync(r => r.AccessionNumber == "DRMC-RAD-TEST-0001"));
        Assert.Equal(patient, study.PatientRecordId);

        var duplicate = await client.PostFormAsync($"/Admin/RadiologyStudies/Create/{patient}", $"/Admin/RadiologyStudies/Create/{patient}", Study("DRMC-rad-TEST-0001"));
        Assert.Contains("already uses this accession number", await duplicate.Content.ReadAsStringAsync());

        var editPage = await client.GetStringAsync($"/Admin/RadiologyStudies/Edit/{study.Id}");
        Assert.Contains(">X-ray</option>", editPage);
        var version = System.Text.RegularExpressions.Regex.Match(editPage, "name=\"RowVersion\" value=\"([^\"]+)\"").Groups[1].Value;
        version = WebUtility.HtmlDecode(version);
        var edited = await client.PostFormAsync($"/Admin/RadiologyStudies/Edit/{study.Id}", $"/Admin/RadiologyStudies/Edit/{study.Id}", Study("DRMC-RAD-TEST-0001", rowVersion: version, findings: "Edited synthetic findings."));
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        var stale = await client.PostFormAsync($"/Admin/RadiologyStudies/Edit/{study.Id}", $"/Admin/RadiologyStudies/Edit/{study.Id}", Study("DRMC-RAD-TEST-0001", rowVersion: version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var audit = await Db(db => db.AdminAuditLogs.Where(a => a.Entity == "RadiologyStudies" && a.RecordKey == study.Id.ToString()).ToListAsync());
        Assert.Contains(audit, a => a.Action == "Create");
        Assert.Contains(audit, a => a.Action == "Edit" && a.ChangedFields.Contains("Findings"));
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
            var record = new PatientRecord { FullName = "Synthetic Deletion Blocked" };
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
