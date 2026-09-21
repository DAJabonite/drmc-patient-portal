using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DrmcPatientPortal.Controllers;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.Sqlite;
using Moq;

namespace DrmcPatientPortal.Tests;

public class ClientRequirementsTests
{
    [Fact]
    public async Task Migration_PreservesExistingPatients_AndEnforcesOneApplicationPerPatient()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20260912091834_RestoreEncountersAndHardenPortal");
        db.Users.Add(new ApplicationUser { Id = "existing-patient", FullName = "Migration Test" });
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        Assert.Equal("Migration Test", (await db.Users.SingleAsync()).FullName);
        db.SubsidyApplications.Add(new() { PatientUserId = "existing-patient" });
        await db.SaveChangesAsync();
        db.SubsidyApplications.Add(new() { PatientUserId = "existing-patient" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static UserManager<ApplicationUser> Users(string id = "patient-a")
    {
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        users.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(new ApplicationUser { Id = id });
        return users.Object;
    }

    private static T Context<T>(T controller) where T : Controller
    {
        var http = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
        return controller;
    }

    [Fact]
    public void DswdServices_ReturnsInformationalRequirementsView()
    {
        using var db = Database();
        var controller = Context(new MalasakitController(db, Users(), Mock.Of<IAuditLogService>()));

        var result = Assert.IsType<ViewResult>(controller.DSWDServices());

        Assert.Null(result.Model);
    }

    [Fact]
    public void DrmcMalasakitServices_ReturnsInformationalChecklistView()
    {
        using var db = Database();
        var controller = Context(new MalasakitController(db, Users(), Mock.Of<IAuditLogService>()));

        var result = Assert.IsType<ViewResult>(controller.DRMCMalasakitServices());

        Assert.Null(result.Model);
    }

    [Fact]
    public async Task History_IsOwnerOnly_AndGroupsOutpatientEmergencyAndAdmissions()
    {
        using var db = Database();
        foreach (var type in Enum.GetValues<EncounterType>())
            db.ClinicalEncounters.Add(new() { PatientUserId = "patient-a", Type = type });
        db.ClinicalEncounters.Add(new() { PatientUserId = "patient-b", Type = EncounterType.Emergency });
        await db.SaveChangesAsync();
        var audit = new Mock<IAuditLogService>();
        var controller = Context(new MedicalHistoryController(db, Users(), audit.Object));
        var result = Assert.IsType<ViewResult>(await controller.Index("er"));
        var model = Assert.IsType<MedicalHistoryViewModel>(result.Model);
        Assert.Equal("ER", model.SelectedCategory);
        Assert.Equal(4, model.Records.Count);
        Assert.All(model.Records, e => Assert.Equal("patient-a", e.PatientUserId));
        Assert.Equal(2, model.Records.Count(e => MedicalHistoryViewModel.CategoryFor(e.Type) == "OPD"));
        Assert.Single(model.Records, e => MedicalHistoryViewModel.CategoryFor(e.Type) == "ER");
        Assert.Single(model.Records, e => MedicalHistoryViewModel.CategoryFor(e.Type) == "ADMITTED");
        Assert.IsType<BadRequestResult>(await controller.Index("unknown"));
        audit.Verify(x => x.LogAsync("patient-a", "VIEW_MEDICAL_HISTORY", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Encounters_CustomDateRange_IsInclusiveAndOwnerOnly()
    {
        using var db = Database();
        db.ClinicalEncounters.AddRange(
            new ClinicalEncounter { PatientUserId = "patient-a", EncounterDate = new DateTime(2026, 1, 31, 23, 59, 0) },
            new ClinicalEncounter { PatientUserId = "patient-a", EncounterDate = new DateTime(2026, 2, 1, 8, 0, 0) },
            new ClinicalEncounter { PatientUserId = "patient-a", EncounterDate = new DateTime(2026, 2, 28, 17, 30, 0) },
            new ClinicalEncounter { PatientUserId = "patient-a", EncounterDate = new DateTime(2026, 3, 1, 0, 0, 0) },
            new ClinicalEncounter { PatientUserId = "patient-b", EncounterDate = new DateTime(2026, 2, 15, 12, 0, 0) });
        await db.SaveChangesAsync();

        var controller = Context(new VisitsController(db, Users(), Mock.Of<IAuditLogService>()));
        var result = Assert.IsType<ViewResult>(await controller.Index(
            null,
            "custom",
            new DateTime(2026, 2, 1),
            new DateTime(2026, 2, 28)));
        var model = Assert.IsType<VisitsIndexViewModel>(result.Model);

        Assert.Equal(2, model.Encounters.Count);
        Assert.All(model.Encounters, encounter => Assert.Equal("patient-a", encounter.PatientUserId));
        Assert.Contains(model.Encounters, encounter => encounter.EncounterDate == new DateTime(2026, 2, 1, 8, 0, 0));
        Assert.Contains(model.Encounters, encounter => encounter.EncounterDate == new DateTime(2026, 2, 28, 17, 30, 0));
    }

    [Fact]
    public async Task Encounters_TypeFilter_GroupsOpdTeleconsultationEmergencyAndInpatient()
    {
        using var db = Database();
        db.ClinicalEncounters.AddRange(
            new ClinicalEncounter { PatientUserId = "patient-a", Type = EncounterType.OpdConsultation },
            new ClinicalEncounter { PatientUserId = "patient-a", Type = EncounterType.Teleconsultation },
            new ClinicalEncounter { PatientUserId = "patient-a", Type = EncounterType.Emergency },
            new ClinicalEncounter { PatientUserId = "patient-a", Type = EncounterType.Inpatient },
            new ClinicalEncounter { PatientUserId = "patient-b", Type = EncounterType.OpdConsultation });
        await db.SaveChangesAsync();

        var controller = Context(new VisitsController(db, Users(), Mock.Of<IAuditLogService>()));
        var result = Assert.IsType<ViewResult>(await controller.Index("OPD", null));
        var model = Assert.IsType<VisitsIndexViewModel>(result.Model);

        Assert.Equal(2, model.Encounters.Count);
        Assert.All(model.Encounters, encounter => Assert.Equal("patient-a", encounter.PatientUserId));
        Assert.All(model.Encounters, encounter => Assert.Contains(encounter.Type, new[] { EncounterType.OpdConsultation, EncounterType.Teleconsultation }));
        Assert.Equal("OPD", model.SelectedType);
    }

    [Fact]
    public async Task Application_PersistsPendingOutcomes_AndDuplicateSubmissionKeepsOriginal()
    {
        using var db = Database();
        var controller = Context(new MalasakitController(db, Users(), Mock.Of<IAuditLogService>()));
        var input = new SubsidyApplicationInput { Need = SubsidyNeed.Medicines, ReportedPayment = BillPaymentStatus.Paid,
            AcknowledgedReview = true, GovernmentIdReady = true };
        Assert.IsType<RedirectToActionResult>(await controller.Apply(input));
        await controller.Apply(new() { Need = SubsidyNeed.HospitalCare, ReportedPayment = BillPaymentStatus.Unpaid, AcknowledgedReview = true });
        var application = Assert.Single(await db.SubsidyApplications.ToListAsync());
        Assert.Equal(SubsidyNeed.Medicines, application.Need);
        Assert.Equal(BillPaymentStatus.Paid, application.ReportedPayment);
        Assert.Equal(BillPaymentStatus.Unconfirmed, application.ConfirmedPayment);
        Assert.Equal(SubsidyEligibility.PendingReview, application.Eligibility);
        Assert.Equal(SubsidyCoverage.Unconfirmed, application.Coverage);
        Assert.True(application.GovernmentIdReady);
    }

    [Fact]
    public async Task Requirements_UpdateOnlyOwner_AndDoNotApproveEligibilityOrCoverage()
    {
        using var db = Database();
        db.SubsidyApplications.AddRange(new() { PatientUserId = "patient-a" }, new() { PatientUserId = "patient-b" });
        await db.SaveChangesAsync();
        var controller = Context(new MalasakitController(db, Users(), Mock.Of<IAuditLogService>()));
        await controller.Requirements(new() { GovernmentIdReady = true, ClinicalDocumentReady = true, CostDocumentReady = true, IndigencyDocumentReady = true });
        var model = Assert.IsType<SubsidyApplication>(Assert.IsType<ViewResult>(await controller.Status()).Model);
        Assert.Equal("patient-a", model.PatientUserId);
        Assert.True(model.CostDocumentReady);
        Assert.False((await db.SubsidyApplications.SingleAsync(a => a.PatientUserId == "patient-b")).GovernmentIdReady);
        Assert.Equal(SubsidyEligibility.PendingReview, model.Eligibility);
        Assert.Equal(SubsidyCoverage.Unconfirmed, model.Coverage);
    }

    [Fact]
    public async Task AuditFailure_PreventsApplicationWritesAndHistoryDisclosure()
    {
        using var db = Database();
        var audit = new Mock<IAuditLogService>();
        audit.Setup(x => x.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Audit unavailable"));
        var controller = Context(new MalasakitController(db, Users(), audit.Object));
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Apply(new() {
            Need = SubsidyNeed.Diagnostics, ReportedPayment = BillPaymentStatus.Unconfirmed, AcknowledgedReview = true }));
        Assert.Empty(await db.SubsidyApplications.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Context(new MedicalHistoryController(db, Users(), audit.Object)).Index(null));
    }

    [Fact]
    public async Task InvalidApplication_DoesNotPersist()
    {
        using var db = Database();
        var controller = Context(new MalasakitController(db, Users(), Mock.Of<IAuditLogService>()));
        controller.ModelState.AddModelError("Need", "Required");
        Assert.IsType<ViewResult>(await controller.Apply(new SubsidyApplicationInput()));
        Assert.Empty(await db.SubsidyApplications.ToListAsync());
        var errors = new List<ValidationResult>();
        var input = new SubsidyApplicationInput { Need = (SubsidyNeed)900, ReportedPayment = (BillPaymentStatus)900 };
        Assert.False(Validator.TryValidateObject(input, new ValidationContext(input), errors, true));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(input.Need)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(input.ReportedPayment)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(input.AcknowledgedReview)));
    }
}
