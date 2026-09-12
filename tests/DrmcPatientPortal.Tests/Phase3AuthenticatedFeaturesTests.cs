using System.Security.Claims;
using DrmcPatientPortal.Controllers;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.DataProtection;
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
        userManager.Setup(m => m.GetUserId(It.IsAny<ClaimsPrincipal>())).Returns(user.Id);

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
    public async Task LabResultsController_Index_ReturnsPatientLabResultsWithStatus()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, user) = CreateMockUserManager(db);
        var auditMock = new Mock<IAuditLogService>();

        db.LabResults.Add(
            new LabResult { PatientUserId = user.Id, TestName = "HbA1c", Category = LabCategory.SpecialDiagnostics, Status = "In progress", AccessionNumber = "A-INPROG" }
        );
        await db.SaveChangesAsync();

        var controller = new LabResultsController(db, userManager, auditMock.Object)
        {
            ControllerContext = CreateControllerContext()
        };

        var result = await controller.Index(null, null) as ViewResult;
        Assert.NotNull(result);

        var model = Assert.IsType<LabResultsIndexViewModel>(result.Model);
        Assert.Single(model.LabResults);
        Assert.Equal("HbA1c", model.LabResults[0].TestName);
        Assert.Equal(1, model.TotalInProgress);
    }

    [Fact]
    public async Task LabResultsController_Index_SearchTerm_FiltersResults()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, user) = CreateMockUserManager(db);
        var auditMock = new Mock<IAuditLogService>();

        db.LabResults.AddRange(
            new LabResult { PatientUserId = user.Id, TestName = "Complete Blood Count", Category = LabCategory.Hematology, Status = "Available", AccessionNumber = "A21" },
            new LabResult { PatientUserId = user.Id, TestName = "Fasting Blood Sugar", Category = LabCategory.ClinicalChemistry, Status = "Available", AccessionNumber = "A22" }
        );
        await db.SaveChangesAsync();

        var controller = new LabResultsController(db, userManager, auditMock.Object)
        {
            ControllerContext = CreateControllerContext()
        };

        var result = await controller.Index(null, "Fasting") as ViewResult;
        Assert.NotNull(result);

        var model = Assert.IsType<LabResultsIndexViewModel>(result.Model);
        Assert.Single(model.LabResults);
        Assert.Equal("Fasting Blood Sugar", model.LabResults[0].TestName);
    }

    [Fact]
    public async Task LabResultsController_Details_ReturnsLabResultAndLogsAudit()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, user) = CreateMockUserManager(db);
        var auditMock = new Mock<IAuditLogService>();

        var lab = new LabResult
        {
            PatientUserId = user.Id,
            TestName = "Complete Blood Count",
            Category = LabCategory.Hematology,
            Status = "Available",
            AccessionNumber = "LAB-001"
        };
        db.LabResults.Add(lab);
        await db.SaveChangesAsync();

        var controller = new LabResultsController(db, userManager, auditMock.Object)
        {
            ControllerContext = CreateControllerContext()
        };

        var result = await controller.Details(lab.Id) as ViewResult;
        Assert.NotNull(result);

        var model = Assert.IsType<LabResult>(result.Model);
        Assert.Equal("LAB-001", model.AccessionNumber);

        auditMock.Verify(a => a.LogAsync(user.Id, "VIEW_LAB_REPORT", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task MedicationsController_RequestRefill_RecordsPendingRequestWithoutDecrementing()
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
        Assert.Equal(2, updatedRx.RefillsRemaining);
        Assert.Single(updatedRx.RefillRequests);
        Assert.Equal(RefillStatus.Requested, updatedRx.RefillRequests.First().Status);
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
        var intake = await db.TriageIntakes.SingleAsync();
        Assert.True(intake.HasEmergencyRedFlags);
        Assert.Equal(TriageAcuity.UrgentEmergency, intake.AcuityLevel);
    }

    [Fact]
    public async Task EncountersController_Details_IsOwnerIsolatedAndIncludesLabs()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, user) = CreateMockUserManager(db);
        var auditMock = new Mock<IAuditLogService>();
        var owned = new ClinicalEncounter { PatientUserId = user.Id, EncounterReference = "ENC-OWNED", Department = "Internal Medicine", PrimaryDiagnosis = "Hypertension" };
        owned.LabResults.Add(new LabResult { PatientUserId = user.Id, AccessionNumber = "LAB-ENC", TestName = "CBC" });
        var other = new ClinicalEncounter { PatientUserId = "other", EncounterReference = "ENC-OTHER", Department = "Surgery", PrimaryDiagnosis = "Follow-up" };
        db.ClinicalEncounters.AddRange(owned, other);
        await db.SaveChangesAsync();
        var controller = new EncountersController(db, userManager, auditMock.Object) { ControllerContext = CreateControllerContext() };

        var result = Assert.IsType<ViewResult>(await controller.Details(owned.Id));
        Assert.Single(Assert.IsType<ClinicalEncounter>(result.Model).LabResults);
        Assert.IsType<NotFoundResult>(await controller.Details(other.Id));
        auditMock.Verify(x => x.LogAsync(user.Id, "VIEW_ENCOUNTER_SUMMARY", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void AppointmentAccessToken_IsHighEntropyAndExpires()
    {
        var service = new AppointmentAccessService(new EphemeralDataProtectionProvider());
        var appointment = new Appointment { Id = 7, ScheduledAt = DateTime.Now.AddDays(2) };
        var token = service.CreateToken(appointment);
        Assert.True(token.Length >= 43);
        Assert.DoesNotContain(token, appointment.PublicAccessTokenHash!, StringComparison.Ordinal);
        Assert.True(service.ValidateToken(appointment, token));
        Assert.False(service.ValidateToken(appointment, token + "x"));
        appointment.PublicAccessExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        Assert.False(service.ValidateToken(appointment, token));
    }

    [Fact]
    public async Task AppointmentsController_AllowsLegacyOwnerButDeniesReferenceOnlyAccess()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, user) = CreateMockUserManager(db);
        var ownerAppointment = new Appointment { PatientUserId = user.Id, BookingReference = "DRMC-OWNER", ScheduledAt = DateTime.Now.AddDays(1) };
        var otherAppointment = new Appointment { PatientUserId = "other-user", BookingReference = "DRMC-OTHER", ScheduledAt = DateTime.Now.AddDays(1) };
        db.Appointments.AddRange(ownerAppointment, otherAppointment);
        await db.SaveChangesAsync();

        var access = new Mock<IAppointmentAccessService>();
        var qr = new Mock<IQrCodeService>();
        qr.Setup(x => x.GenerateSvgQrCode(It.IsAny<string>())).Returns("<svg></svg>");
        var controller = new AppointmentsController(db, userManager, Mock.Of<ISmsSender>(), Mock.Of<IEmailSender>(), qr.Object, access.Object)
        {
            ControllerContext = CreateControllerContext()
        };

        Assert.IsType<ViewResult>(await controller.Confirmation(ownerAppointment.BookingReference));
        Assert.IsType<NotFoundResult>(await controller.Confirmation(otherAppointment.BookingReference));
        Assert.IsType<NotFoundResult>(await controller.Cancel(otherAppointment.Id));
        Assert.NotEqual("Cancelled", otherAppointment.Status);
    }
}
