using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DrmcPatientPortal.Tests;

public class DbInitializerTests
{
    [Fact]
    public async Task Fresh_database_has_valid_dependencies_and_reseeding_preserves_patient_data()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        DbInitializer.Initialize(db, users);

        Assert.False(db.Database.HasPendingModelChanges());
        var patient = await users.FindByEmailAsync("patient@drmc.doh.gov.ph");
        Assert.NotNull(patient);
        var intake = Assert.Single(await db.TriageIntakes.Include(t => t.Appointment).ToListAsync());
        Assert.Equal(patient.Id, intake.PatientUserId);
        Assert.Equal(intake.PatientUserId, intake.Appointment.PatientUserId);
        Assert.Empty(await db.RefillRequests.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.All(await db.LabResults.Include(l => l.Encounter).ToListAsync(), lab =>
        {
            if (lab.Encounter is not null) Assert.Equal(lab.PatientUserId, lab.Encounter.PatientUserId);
        });
        var visitCount = await db.ClinicalEncounters.CountAsync();
        var labCount = await db.LabResults.CountAsync();
        var doseCount = await db.MedicationDoseSchedules.CountAsync();
        patient.Address = "Patient-edited address";
        intake.ChiefComplaint = "Patient-edited intake";
        db.SubsidyApplications.Add(new() { PatientUserId = patient.Id, Need = SubsidyNeed.Diagnostics });
        await db.SaveChangesAsync();

        DbInitializer.Initialize(db, users);
        db.ChangeTracker.Clear();

        Assert.Equal(visitCount, await db.ClinicalEncounters.CountAsync());
        Assert.Equal(labCount, await db.LabResults.CountAsync());
        Assert.Equal(doseCount, await db.MedicationDoseSchedules.CountAsync());
        Assert.Equal("Patient-edited intake", (await db.TriageIntakes.SingleAsync()).ChiefComplaint);
        Assert.Equal("Patient-edited address", (await db.Users.SingleAsync(u => u.Id == patient.Id)).Address);
        var application = await db.SubsidyApplications.SingleAsync();
        Assert.Equal(SubsidyEligibility.PendingReview, application.Eligibility);
        Assert.Equal(SubsidyCoverage.Unconfirmed, application.Coverage);
        Assert.Equal(BillPaymentStatus.Unconfirmed, application.ConfirmedPayment);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check";
        await using var violations = await command.ExecuteReaderAsync();
        Assert.False(await violations.ReadAsync());
    }
}
