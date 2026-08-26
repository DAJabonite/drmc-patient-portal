using System.Security.Claims;
using DrmcPatientPortal.Controllers;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DrmcPatientPortal.Tests;

public class Phase3GovernanceTests
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
            Id = "gov-patient-001",
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

    private ControllerContext CreateControllerContext()
    {
        var httpContext = new DefaultHttpContext();
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "gov-patient-001"),
            new Claim(ClaimTypes.Name, "patient@drmc.doh.gov.ph")
        }, "mock"));

        httpContext.User = claims;

        return new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Fact]
    public void SetLanguage_SetsRequestCultureCookieAndRedirects()
    {
        var controller = new HomeController();
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = controller.SetLanguage("ceb", "/Patient/Home") as LocalRedirectResult;
        Assert.NotNull(result);
        Assert.Equal("/Patient/Home", result.Url);

        var cookieHeader = httpContext.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains(CookieRequestCultureProvider.DefaultCookieName, cookieHeader);
        Assert.Contains("c%3Dceb%7Cuic%3Dceb", cookieHeader);
    }

    [Fact]
    public async Task ProxyController_Add_CreatesConsentLogEntry()
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
            FullName = "Corazon Santos",
            DateOfBirth = new DateTime(1958, 5, 10),
            Gender = "Female",
            Relationship = RelationshipType.Parent,
            IdType = "OSCA Senior Citizen ID",
            IdNumber = "OSCA-12345",
            StatutoryConsentAgreed = true
        };

        var result = await controller.Add(model) as RedirectToActionResult;
        Assert.NotNull(result);

        var consentLog = await db.ConsentLogEntries.FirstOrDefaultAsync(c => c.GuardianUserId == user.Id);
        Assert.NotNull(consentLog);
        Assert.Equal(ConsentEventType.Granted, consentLog.EventType);
        Assert.Equal("Corazon Santos", consentLog.DependentName);
        Assert.Contains("RA 10173", consentLog.ConsentDeclarationText);
    }

    [Fact]
    public async Task ProxyController_Revoke_CreatesRevocationConsentLogAndRemovesProfile()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, user) = CreateMockUserManager(db);
        var auditMock = new Mock<IAuditLogService>();

        var dep = new DependentProfile
        {
            GuardianUserId = user.Id,
            FullName = "Baby Boy Santos",
            DateOfBirth = DateTime.UtcNow.AddYears(-2),
            Gender = "Male",
            Relationship = RelationshipType.Child,
            StatutoryConsentAgreed = true
        };
        db.DependentProfiles.Add(dep);
        await db.SaveChangesAsync();

        var controller = new ProxyController(db, userManager, auditMock.Object)
        {
            ControllerContext = CreateControllerContext(),
            TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
        };

        var result = await controller.Revoke(dep.Id) as RedirectToActionResult;
        Assert.NotNull(result);

        var remainingDep = await db.DependentProfiles.FirstOrDefaultAsync(d => d.Id == dep.Id);
        Assert.Null(remainingDep);

        var revokeLog = await db.ConsentLogEntries.FirstOrDefaultAsync(c => c.GuardianUserId == user.Id && c.EventType == ConsentEventType.Revoked);
        Assert.NotNull(revokeLog);
        Assert.Equal("Baby Boy Santos", revokeLog.DependentName);
    }

    [Fact]
    public async Task PatientController_Audit_ReturnsAuditLogsIsolatedToUser()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, user) = CreateMockUserManager(db);

        db.AuditLogs.AddRange(
            new AuditLog { UserId = user.Id, Action = "VIEW_LAB_REPORT", Resource = "LabResult/1", Details = "Viewed CBC Report", Timestamp = DateTime.UtcNow },
            new AuditLog { UserId = "other-user-999", Action = "VIEW_LAB_REPORT", Resource = "LabResult/2", Details = "Other patient report", Timestamp = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var controller = new PatientController(db, userManager)
        {
            ControllerContext = CreateControllerContext()
        };

        var result = await controller.Audit() as ViewResult;
        Assert.NotNull(result);

        var model = Assert.IsType<PatientAuditViewModel>(result.Model);
        Assert.Single(model.Logs);
        Assert.Equal("LabResult/1", model.Logs[0].Resource);
    }

    [Fact]
    public void HomeController_OpdGuide_ReturnsAllStepsAndMetadata()
    {
        var controller = new HomeController();
        var result = controller.OpdGuide() as ViewResult;
        Assert.NotNull(result);

        var model = Assert.IsType<OpdGuideViewModel>(result.Model);
        Assert.Equal(6, model.Steps.Count);
        Assert.Equal("OPD Triage & Screening Desk", model.Steps[0].Title);
        Assert.Equal("OPD Pharmacy Dispensing Window", model.Steps[5].Title);
        Assert.False(string.IsNullOrWhiteSpace(model.IntroLine));
        Assert.False(string.IsNullOrWhiteSpace(model.KioskReferralLine));
    }
}
