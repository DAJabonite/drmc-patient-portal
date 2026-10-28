using System.Net;
using DrmcPatientPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed class PortalTests(PortalFactory factory)
{
    private WebApplicationFactory<Program>? reportHost;
    private WebApplicationFactory<Program> ReportHost => reportHost ??= factory.WithWebHostBuilder(b => b.UseSetting("PatientResults:ShowFullResults", "true"));
    private HttpClient ReportClient() => ReportHost.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private async Task<int> SeedVisibleLab()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var patient = await db.PatientRecords.SingleAsync(p => p.PortalUser!.Email == PortalFactory.PrimaryEmail);
        var lab = new LabResult
        {
            PatientRecordId = patient.Id, AccessionNumber = "PORTAL-TEST-" + Guid.NewGuid().ToString("N"),
            TestName = "Synthetic special diagnostics", Category = LabCategory.SpecialDiagnostics, Status = "Available",
            CollectedAt = ClinicalClock.Today.AddDays(-2), ReleasedAt = ClinicalClock.Today.AddDays(-1), ReportFileName = Guid.NewGuid().ToString("N")
        };
        db.LabResults.Add(lab);
        await db.SaveChangesAsync();
        return lab.Id;
    }
    [Theory]
    [InlineData("/")]
    [InlineData("/Directory")]
    [InlineData("/Advisories")]
    [InlineData("/Malasakit")]
    [InlineData("/Home/OpdGuide")]
    [InlineData("/Home/Privacy")]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Identity/Account/Register")]
    public async Task Public_pages_load(string path)
    {
        var response = await factory.CreatePortalClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/Patient/Home")]
    [InlineData("/Patient/Visits")]
    [InlineData("/Patient/LabResults")]
    [InlineData("/Patient/Medications")]
    public async Task Patient_pages_require_sign_in(string path)
    {
        var response = await factory.CreatePortalClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Unknown_page_returns_friendly_not_found_page()
    {
        var response = await factory.CreatePortalClient().GetAsync("/Home/ThisPageDoesNotExist");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Page not found", html);
    }

    [Fact]
    public async Task Content_security_policy_disallows_inline_scripts()
    {
        var response = await factory.CreatePortalClient().GetAsync("/");
        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        var scriptSrc = policy.Split(';').Select(d => d.Trim()).Single(d => d.StartsWith("script-src"));
        Assert.Equal("script-src 'self'", scriptSrc);
    }

    [Fact]
    public async Task Patient_can_open_own_records_but_another_patient_cannot()
    {
        var labId = await SeedVisibleLab();
        var owner = ReportClient();
        await owner.SignInSuccessfullyAsync(PortalFactory.PrimaryEmail, PortalFactory.Password);
        var visit = PortalClient.Links(await owner.GetStringAsync("/Patient/Visits"), "/Patient/Visits/Details/").First();
        var lab = $"/Patient/LabResults/Details/{labId}";
        Assert.Contains($"action=\"/Patient/LabResults/Report/{labId}\"", await owner.GetStringAsync("/Patient/LabResults"));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(visit)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(lab)).StatusCode);

        var other = ReportClient();
        await other.SignInSuccessfullyAsync(PortalFactory.EmptyEmail, PortalFactory.Password);
        foreach (var path in new[] { visit, lab })
        {
            var response = await other.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain(PortalFactory.PrimaryEmail, await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Lab_results_show_readable_category_names()
    {
        var labId = await SeedVisibleLab();
        var client = ReportClient();
        await client.SignInSuccessfullyAsync(PortalFactory.PrimaryEmail, PortalFactory.Password);
        var html = await client.GetStringAsync("/Patient/LabResults");
        Assert.Contains(">Special Diagnostics</option>", html);
        Assert.DoesNotContain(">SpecialDiagnostics</option>", html);
        Assert.Contains($"action=\"/Patient/LabResults/Report/{labId}\"", html);
    }

    [Fact]
    public async Task Unconfirmed_account_cannot_sign_in()
    {
        const string email = "unconfirmed.patient@fixtures.test";
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            if (await users.FindByEmailAsync(email) is null)
            {
                var result = await users.CreateAsync(new ApplicationUser
                {
                    UserName = email, Email = email, EmailConfirmed = false,
                    FirstName = "Una", LastName = "Confirmed", FullName = "Una Confirmed",
                }, PortalFactory.Password);
                Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        var client = factory.CreatePortalClient();
        var response = await client.SignInAsync(email, PortalFactory.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid login attempt.", html);
        Assert.Contains("/Identity/Account/ResendEmailConfirmation", html);
        Assert.DoesNotContain("id=\"email-not-confirmed\"", html);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Patient/Home")).StatusCode);
    }
}
