using System.Security.Claims;
using System.Runtime.Versioning;
using System.Text;
using DrmcPatientPortal.Areas.Identity.Pages.Account;
using DrmcPatientPortal.Controllers;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DrmcPatientPortal.Tests;

[SupportedOSPlatform("windows")]
public class IdDocumentRegistrationTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private (UserManager<ApplicationUser> userManager, ApplicationUser user, IUserEmailStore<ApplicationUser> userStore) CreateMockUserManager(ApplicationDbContext db)
    {
        var user = new ApplicationUser
        {
            Id = "patient-id-test-001",
            UserName = "test.patient@drmc.doh.gov.ph",
            Email = "test.patient@drmc.doh.gov.ph",
            FullName = "Maria Santos Dela Cruz",
            FirstName = "Maria",
            MiddleName = "Santos",
            LastName = "Dela Cruz",
            ContactNumber = "+63 917 123 4567",
            IdType = PhilippineIdTypes.PhilSys,
            IdNumber = "1234-5678-9012-3456"
        };
        db.Users.Add(user);
        db.SaveChanges();

        var store = new Mock<IUserEmailStore<ApplicationUser>>();
        store.As<IUserStore<ApplicationUser>>();

        var userManager = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(m => m.SupportsUserEmail).Returns(true);
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
        userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
                   .ReturnsAsync(IdentityResult.Success);
        userManager.Setup(m => m.GenerateEmailConfirmationTokenAsync(It.IsAny<ApplicationUser>()))
                   .ReturnsAsync("mock-confirm-token");
        userManager.Setup(m => m.GetUserIdAsync(It.IsAny<ApplicationUser>()))
                   .ReturnsAsync((ApplicationUser u) => u.Id ?? "new-user-id");

        return (userManager.Object, user, store.Object);
    }

    [Fact]
    public void PhilippineIdTypes_ContainsAll8OfficialTypesWithAccurateMetadata()
    {
        var types = PhilippineIdTypes.All;
        Assert.Equal(8, types.Count);

        var philSys = PhilippineIdTypes.GetByName(PhilippineIdTypes.PhilSys);
        Assert.NotNull(philSys);
        Assert.True(philSys.NeedsBackPhoto);
        Assert.True(philSys.SupportsName);
        Assert.True(philSys.SupportsDob);
        Assert.True(philSys.SupportsSex);
        Assert.True(philSys.SupportsBloodType);

        var dl = PhilippineIdTypes.GetByName(PhilippineIdTypes.DriversLicense);
        Assert.NotNull(dl);
        Assert.False(dl.NeedsBackPhoto);
        Assert.True(dl.SupportsBloodType);

        var passport = PhilippineIdTypes.GetByName(PhilippineIdTypes.Passport);
        Assert.NotNull(passport);
        Assert.False(passport.NeedsBackPhoto);
        Assert.False(passport.SupportsAddress);

        var philHealth = PhilippineIdTypes.GetByName(PhilippineIdTypes.PhilHealth);
        Assert.NotNull(philHealth);
        Assert.False(philHealth.SupportsDob);
        Assert.False(philHealth.SupportsBloodType);

        var sss = PhilippineIdTypes.GetByName(PhilippineIdTypes.SssGsis);
        Assert.NotNull(sss);
        Assert.False(sss.SupportsDob);

        var prc = PhilippineIdTypes.GetByName(PhilippineIdTypes.PrcId);
        Assert.NotNull(prc);
        Assert.False(prc.SupportsDob);
    }

    [Fact]
    public void ExtractionService_InitializesCorrectly()
    {
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(AppDomain.CurrentDomain.BaseDirectory);
        var mockLogger = new Mock<ILogger<TesseractIdDocumentExtractionService>>();
        var service = new TesseractIdDocumentExtractionService(mockEnv.Object, mockLogger.Object);

        Assert.NotNull(service);
    }

    [Fact]
    public async Task ExtractionService_ExtractsDriversLicenseFieldsFromImage()
    {
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(AppDomain.CurrentDomain.BaseDirectory);
        var mockLogger = new Mock<ILogger<TesseractIdDocumentExtractionService>>();
        var service = new TesseractIdDocumentExtractionService(mockEnv.Object, mockLogger.Object);

        string dlText = "LAND TRANSPORTATION OFFICE\nDRIVER'S LICENSE\nLicense No: D02-18-012345\nLast Name: DELA CRUZ\nFirst Name: JUAN\nMiddle Name: SANTOS\nDate of Birth: 1990-05-15\nSex: Male\nBlood Type: O+\nAddress: Tagum City Davao del Norte";
        byte[] imageBytes = GenerateTestImage(dlText, 700, 350);

        var result = await service.ExtractFromBytesAsync(PhilippineIdTypes.DriversLicense, imageBytes);

        Assert.True(result.Success);
        Assert.Equal("D02-18-012345", result.IdNumber.Value);
        Assert.Equal("Dela Cruz", result.LastName.Value);
        Assert.Equal("Juan", result.FirstName.Value);
        Assert.Equal("Santos", result.MiddleName.Value);
        Assert.Equal("Male", result.Sex.Value);
        Assert.Equal("O+", result.BloodType.Value);
        Assert.Equal(new DateTime(1990, 5, 15), result.DateOfBirth.Value);
    }

    [Fact]
    public async Task ExtractionService_ExtractsPhilSysFrontAndBack()
    {
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(AppDomain.CurrentDomain.BaseDirectory);
        var mockLogger = new Mock<ILogger<TesseractIdDocumentExtractionService>>();
        var service = new TesseractIdDocumentExtractionService(mockEnv.Object, mockLogger.Object);

        string frontText = "REPUBLIKA NG PILIPINAS\nPHILIPPINE IDENTIFICATION CARD\n1234-5678-9012-3456\nLast Name / Apelyido: SANTOS\nGiven Names / Mga Pangalan: MARIA CLARA\nMiddle Name / Gitnang Pangalan: DELA CRUZ\nDate of Birth / Petsa ng Kapanganakan: 1995-10-20\nAddress / Tirahan: Apokon Tagum City";
        string backText = "Kasarian / Sex: FEMALE\nUri ng Dugo / Blood Type: A+";

        byte[] frontBytes = GenerateTestImage(frontText, 700, 350);
        byte[] backBytes = GenerateTestImage(backText, 600, 200);

        var result = await service.ExtractFromBytesAsync(PhilippineIdTypes.PhilSys, frontBytes, backBytes);

        Assert.True(result.Success);
        Assert.Equal("1234-5678-9012-3456", result.IdNumber.Value);
        Assert.Equal("Santos", result.LastName.Value);
        Assert.Equal("Maria Clara", result.FirstName.Value);
        Assert.Equal("Dela Cruz", result.MiddleName.Value);
        Assert.Equal("Female", result.Sex.Value);
        Assert.Equal("A+", result.BloodType.Value);
        Assert.Equal(new DateTime(1995, 10, 20), result.DateOfBirth.Value);
    }

    [Fact]
    public async Task ExtractionService_PhilHealth_OnlyExtractsNameAndNumber()
    {
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(AppDomain.CurrentDomain.BaseDirectory);
        var mockLogger = new Mock<ILogger<TesseractIdDocumentExtractionService>>();
        var service = new TesseractIdDocumentExtractionService(mockEnv.Object, mockLogger.Object);

        string philHealthText = "PHILHEALTH IDENTIFICATION CARD\nPIN: 12-345678901-2\nMember Name: DELA CRUZ, JUAN SANTOS";
        byte[] imageBytes = GenerateTestImage(philHealthText, 700, 250);

        var result = await service.ExtractFromBytesAsync(PhilippineIdTypes.PhilHealth, imageBytes);

        Assert.True(result.Success);
        Assert.Equal("12-345678901-2", result.IdNumber.Value);
        Assert.Equal("Dela Cruz", result.LastName.Value);
        Assert.Equal("Juan", result.FirstName.Value);
        Assert.False(result.Sex.Found);
        Assert.False(result.BloodType.Found);
        Assert.False(result.DateOfBirth.Found);
    }

    [Fact]
    public async Task ExtractionService_BlankImage_DoesNotFabricateValues()
    {
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(AppDomain.CurrentDomain.BaseDirectory);
        var mockLogger = new Mock<ILogger<TesseractIdDocumentExtractionService>>();
        var service = new TesseractIdDocumentExtractionService(mockEnv.Object, mockLogger.Object);

        // Blank image without text
        byte[] blankBytes = GenerateTestImage("", 400, 200);

        var result = await service.ExtractFromBytesAsync(PhilippineIdTypes.DriversLicense, blankBytes);

        Assert.False(result.FirstName.Found);
        Assert.False(result.LastName.Found);
        Assert.False(result.IdNumber.Found);
        Assert.False(result.DateOfBirth.Found);
        Assert.False(result.BloodType.Found);
    }

    private static byte[] GenerateTestImage(string text, int width, int height)
    {
        using var bitmap = new System.Drawing.Bitmap(width, height);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.Clear(System.Drawing.Color.White);
            if (!string.IsNullOrWhiteSpace(text))
            {
                using var font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, 14, System.Drawing.FontStyle.Bold);
                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Black);
                g.DrawString(text, font, brush, new System.Drawing.PointF(15, 15));
            }
        }
        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    [Fact]
    public async Task PatientDocumentsController_IdPhoto_DeniesUnauthorizedAndDifferentUser()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, user, userStore) = CreateMockUserManager(db);
        var auditMock = new Mock<IAuditLogService>();
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(AppDomain.CurrentDomain.BaseDirectory);

        var doc = new PatientIdDocument
        {
            PatientUserId = "another-patient-999",
            IdType = PhilippineIdTypes.PhilSys,
            IdNumber = "9999-9999-9999-9999",
            FrontPhotoFileName = "front_test.jpg",
            CapturedAt = DateTime.UtcNow
        };
        db.PatientIdDocuments.Add(doc);
        await db.SaveChangesAsync();

        var controller = new PatientDocumentsController(db, userManager, auditMock.Object, new Mock<IPatientDocumentStorage>().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id),
                        new Claim(ClaimTypes.Name, user.Email!)
                    }, "mock"))
                }
            }
        };

        var result = await controller.IdPhoto(doc.Id, "front");
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RegisterModel_OnPost_CreatesUserAndPatientIdDocument()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, existingUser, userStore) = CreateMockUserManager(db);

        var signInMock = new Mock<SignInManager<ApplicationUser>>(
            userManager,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>().Object,
            null!, null!, null!, null!);

        var loggerMock = new Mock<ILogger<RegisterModel>>();
        var emailMock = new Mock<IEmailSender>();
        var ocrMock = new Mock<IIdDocumentExtractionService>();
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(AppDomain.CurrentDomain.BaseDirectory);

        var httpContext1 = new DefaultHttpContext();
        httpContext1.Request.Scheme = "https";
        httpContext1.Request.Host = new HostString("localhost");

        var urlHelperMock1 = new Mock<IUrlHelper>();
        urlHelperMock1.SetupGet(u => u.ActionContext).Returns(new ActionContext(httpContext1, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        urlHelperMock1.Setup(u => u.RouteUrl(It.IsAny<UrlRouteContext>())).Returns("https://localhost/Account/ConfirmEmail");
        urlHelperMock1.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns("https://localhost/Account/ConfirmEmail");
        urlHelperMock1.Setup(u => u.Content(It.IsAny<string>())).Returns((string s) => s);

        var pageModel = new RegisterModel(
            userManager,
            userStore,
            signInMock.Object,
            loggerMock.Object,
            emailMock.Object,
            ocrMock.Object,
            db,
            new Mock<IPatientDocumentStorage>().Object)
        {
            PageContext = new PageContext
            {
                HttpContext = httpContext1
            },
            Url = urlHelperMock1.Object,
            Input = new RegisterModel.InputModel
            {
                FirstName = "Juan",
                MiddleName = "Santos",
                LastName = "Dela Cruz",
                Email = "juan.delacruz@example.com",
                Mobile = "9175550123",
                IdType = PhilippineIdTypes.DriversLicense,
                IdNumber = "D02-18-012345",
                DateOfBirth = new DateTime(1990, 1, 15),
                Address = "Tagum City, Davao del Norte",
                Sex = "Male",
                BloodType = "O+",
                Password = "Password123!",
                ConfirmPassword = "Password123!",
                PrivacyConsent = true,
                IsManualEntry = false,
                OcrConfidence = 0.92f
            }
        };

        var result = await pageModel.OnPostAsync();
        Assert.NotNull(result);

        // Verify document was recorded
        var doc = await db.PatientIdDocuments.FirstOrDefaultAsync(d => d.IdNumber == "D02-18-012345");
        Assert.NotNull(doc);
        Assert.Equal(PhilippineIdTypes.DriversLicense, doc.IdType);
        Assert.Equal(0.92f, doc.OcrConfidence);
    }

    [Fact]
    public async Task RegisterModel_OnPost_ManualSkip_CreatesUserWithoutPhoto()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, existingUser, userStore) = CreateMockUserManager(db);

        var signInMock = new Mock<SignInManager<ApplicationUser>>(
            userManager,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>().Object,
            null!, null!, null!, null!);

        var loggerMock = new Mock<ILogger<RegisterModel>>();
        var emailMock = new Mock<IEmailSender>();
        var ocrMock = new Mock<IIdDocumentExtractionService>();
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(AppDomain.CurrentDomain.BaseDirectory);

        var httpContext2 = new DefaultHttpContext();
        httpContext2.Request.Scheme = "https";
        httpContext2.Request.Host = new HostString("localhost");

        var urlHelperMock2 = new Mock<IUrlHelper>();
        urlHelperMock2.SetupGet(u => u.ActionContext).Returns(new ActionContext(httpContext2, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        urlHelperMock2.Setup(u => u.RouteUrl(It.IsAny<UrlRouteContext>())).Returns("https://localhost/Account/ConfirmEmail");
        urlHelperMock2.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns("https://localhost/Account/ConfirmEmail");
        urlHelperMock2.Setup(u => u.Content(It.IsAny<string>())).Returns((string s) => s);

        var pageModel = new RegisterModel(
            userManager,
            userStore,
            signInMock.Object,
            loggerMock.Object,
            emailMock.Object,
            ocrMock.Object,
            db,
            new Mock<IPatientDocumentStorage>().Object)
        {
            PageContext = new PageContext
            {
                HttpContext = httpContext2
            },
            Url = urlHelperMock2.Object,
            Input = new RegisterModel.InputModel
            {
                FirstName = "Elderly",
                MiddleName = "Patient",
                LastName = "Manual",
                Email = "elderly.manual@example.com",
                Mobile = "9185550123",
                IdType = PhilippineIdTypes.PhilHealth,
                IdNumber = "12-345678901-2",
                Password = "Password123!",
                ConfirmPassword = "Password123!",
                PrivacyConsent = true,
                IsManualEntry = true
            }
        };

        var result = await pageModel.OnPostAsync();
        Assert.NotNull(result);

        var doc = await db.PatientIdDocuments.FirstOrDefaultAsync(d => d.IdNumber == "12-345678901-2");
        Assert.NotNull(doc);
        Assert.True(doc.IsManualEntry);
        Assert.Null(doc.FrontPhotoFileName);
    }
}
