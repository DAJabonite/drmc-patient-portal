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
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DrmcPatientPortal.Tests;

public class Phase3AuthenticatedFeaturesTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private (UserManager<ApplicationUser> userManager, ApplicationUser user) CreateMockUserManager(ApplicationDbContext db)
    {
        var user = new ApplicationUser
        {
            Id = "test-patient-001",
            UserName = "patient@drmc.doh.gov.ph",
            Email = "patient@drmc.doh.gov.ph",
            FullName = "Maria Clara D. Santos",
            ContactNumber = "+63 917 123 4567"
        };
        db.Users.Add(user);
        db.SaveChanges();

        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);

        return (userManager.Object, user);
    }

    private ControllerContext CreateControllerContext(ISession? session = null)
    {
        var httpContext = new DefaultHttpContext();
        if (session != null)
        {
            httpContext.Session = session;
        }
        else
        {
            httpContext.Session = new Mock<ISession>().Object;
        }

        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-patient-001"),
            new Claim(ClaimTypes.Name, "patient@drmc.doh.gov.ph")
        }, "mock"));

        httpContext.User = claims;

        return new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Fact]
    public async Task LabResultsController_Index_FiltersByCategoryCorrectly()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, user) = CreateMockUserManager(db);
        var auditMock = new Mock<IAuditLogService>();

        db.LabResults.AddRange(
            new LabResult { PatientUserId = user.Id, TestName = "CBC", Category = LabCategory.Hematology, Status = "Available", AccessionNumber = "A1" },
            new LabResult { PatientUserId = user.Id, TestName = "FBS", Category = LabCategory.ClinicalChemistry, Status = "Available", AccessionNumber = "A2" }
        );
        await db.SaveChangesAsync();

        var controller = new LabResultsController(db, userManager, auditMock.Object)
        {
            ControllerContext = CreateControllerContext()
        };

        var result = await controller.Index(LabCategory.Hematology, null) as ViewResult;
        Assert.NotNull(result);

        var model = Assert.IsType<LabResultsIndexViewModel>(result.Model);
        Assert.Single(model.LabResults);
        Assert.Equal("CBC", model.LabResults[0].TestName);
    }

    [Fact]
    public async Task EncountersController_Details_ReturnsEncounterAndLogsAudit()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, user) = CreateMockUserManager(db);
        var auditMock = new Mock<IAuditLogService>();

        var enc = new ClinicalEncounter
        {
            PatientUserId = user.Id,
            EncounterReference = "ENC-001",
            Department = "Internal Medicine",
            PrimaryDiagnosis = "Hypertension",
            EncounterDate = DateTime.UtcNow
        };
        db.ClinicalEncounters.Add(enc);
        await db.SaveChangesAsync();

        var controller = new EncountersController(db, userManager, auditMock.Object)
        {
            ControllerContext = CreateControllerContext()
        };

        var result = await controller.Details(enc.Id) as ViewResult;
        Assert.NotNull(result);

        var model = Assert.IsType<ClinicalEncounter>(result.Model);
        Assert.Equal("ENC-001", model.EncounterReference);

        auditMock.Verify(a => a.LogAsync(user.Id, "VIEW_ENCOUNTER_SUMMARY", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task MedicationsController_RequestRefill_DecrementsRemainingAndCreatesRefillRequest()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, user) = CreateMockUserManager(db);
        var auditMock = new Mock<IAuditLogService>();
        var loggerMock = new Mock<ILogger<MedicationsController>>();

        var rx = new Prescription
        {
            PatientUserId = user.Id,
            RxNumber = "RX-001",
            GenericName = "Metformin",
            Status = PrescriptionStatus.Active,
            RefillsTotal = 3,
            RefillsRemaining = 2
        };
        db.Prescriptions.Add(rx);
        await db.SaveChangesAsync();

        var controller = new MedicationsController(db, userManager, auditMock.Object, loggerMock.Object)
        {
            ControllerContext = CreateControllerContext(),
            TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
        };

        var actionResult = await controller.RequestRefill(rx.Id) as RedirectToActionResult;
        Assert.NotNull(actionResult);
        Assert.Equal(nameof(MedicationsController.RefillStatus), actionResult.ActionName);

        var updatedRx = await db.Prescriptions.Include(p => p.RefillRequests).FirstAsync(p => p.Id == rx.Id);
        Assert.Equal(1, updatedRx.RefillsRemaining);
        Assert.Single(updatedRx.RefillRequests);
        Assert.Equal(RefillStatus.Approved, updatedRx.RefillRequests.First().Status);
    }

    [Fact]
    public async Task TriageController_Submit_RedirectsToEmergencyOnRedFlags()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, user) = CreateMockUserManager(db);
        var auditMock = new Mock<IAuditLogService>();
        var loggerMock = new Mock<ILogger<TriageController>>();

        var appt = new Appointment
        {
            PatientUserId = user.Id,
            BookingReference = "APPT-RED",
            Department = "Internal Medicine",
            ScheduledAt = DateTime.UtcNow.AddDays(1)
        };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();

        var controller = new TriageController(db, userManager, auditMock.Object, loggerMock.Object)
        {
            ControllerContext = CreateControllerContext()
        };

        var model = new TriageSubmissionViewModel
        {
            AppointmentId = appt.Id,
            HasChestPain = true // Red flag triggered
        };

        var actionResult = await controller.Submit(model) as RedirectToActionResult;
        Assert.NotNull(actionResult);
        Assert.Equal(nameof(TriageController.EmergencyWarning), actionResult.ActionName);
    }

    [Fact]
    public async Task MessagesController_Create_CreatesThreadAndInitialMessage()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, user) = CreateMockUserManager(db);
        var auditMock = new Mock<IAuditLogService>();

        var controller = new MessagesController(db, userManager, auditMock.Object)
        {
            ControllerContext = CreateControllerContext(),
            TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
        };

        var model = new NewMessageViewModel
        {
            Department = "Internal Medicine",
            Category = MessageCategory.LabResultClarification,
            Subject = "Fasting Instructions",
            Body = "How long should I fast?"
        };

        var actionResult = await controller.Create(model) as RedirectToActionResult;
        Assert.NotNull(actionResult);
        Assert.Equal(nameof(MessagesController.Thread), actionResult.ActionName);

        var thread = await db.MessageThreads.Include(t => t.Messages).FirstOrDefaultAsync();
        Assert.NotNull(thread);
        Assert.Equal("Fasting Instructions", thread.Subject);
        Assert.Single(thread.Messages);
        Assert.Equal("How long should I fast?", thread.Messages.First().Body);
    }

    [Fact]
    public async Task ProxyController_Add_RegistersDependentProfile()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, user) = CreateMockUserManager(db);
        var auditMock = new Mock<IAuditLogService>();

        var controller = new ProxyController(db, userManager, auditMock.Object)
        {
            ControllerContext = CreateControllerContext(),
            TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
        };

        var model = new AddDependentViewModel
        {
            FullName = "Joshua D. Santos",
            DateOfBirth = DateTime.UtcNow.AddYears(-8),
            Gender = "Male",
            Relationship = RelationshipType.Child,
            StatutoryConsentAgreed = true
        };

        var actionResult = await controller.Add(model) as RedirectToActionResult;
        Assert.NotNull(actionResult);
        Assert.Equal(nameof(ProxyController.Index), actionResult.ActionName);

        var dep = await db.DependentProfiles.FirstOrDefaultAsync(d => d.GuardianUserId == user.Id);
        Assert.NotNull(dep);
        Assert.Equal("Joshua D. Santos", dep.FullName);
        Assert.True(dep.StatutoryConsentAgreed);
    }
}
