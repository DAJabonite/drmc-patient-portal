using System.Net;
using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed class AccessMatrixTests(PortalFactory factory)
{
    // Admin controller families, each with representative GET URLs.
    private static readonly (string Family, string[] Paths)[] Families =
    [
        ("Home", ["/Admin"]),
        ("Patients", ["/Admin/Patients", "/Admin/Patients/Create"]),
        ("ClinicalEncounters", ["/Admin/ClinicalEncounters", "/Admin/ClinicalEncounters/Create"]),
        ("LabResults", ["/Admin/LabResults", "/Admin/LabResults/Create"]),
        ("LabResultItems", ["/Admin/LabResultItems", "/Admin/LabResultItems/Create"]),
        ("RadiologyStudies", ["/Admin/RadiologyStudies", "/Admin/RadiologyStudies/Create"]),
        ("Prescriptions", ["/Admin/Prescriptions"]),
        ("MedicationDoseSchedules", ["/Admin/MedicationDoseSchedules"]),
        ("PatientAllergies", ["/Admin/PatientAllergies"]),
        ("Doctors", ["/Admin/Doctors", "/Admin/Doctors/Create"]),
        ("PublicAdvisories", ["/Admin/PublicAdvisories"]),
        ("Imports", ["/Admin/Imports"]),
        ("Audit", ["/Admin/Audit"]),
        ("StaffAccess", ["/Admin/StaffAccess"]),
    ];

    private static readonly Dictionary<string, string[]> StaffFamilies = new()
    {
        ["LabStaff"] = ["Home", "LabResults", "LabResultItems"],
        ["RadiologyStaff"] = ["Home", "RadiologyStudies"],
    };

    private async Task<HttpClient> ClientFor(string actor) => actor switch
    {
        "Admin" => await StaffAccounts.AdminAsync(factory),
        "LabStaff" => await StaffAccounts.LabAsync(factory),
        "RadiologyStaff" => await StaffAccounts.RadiologyAsync(factory),
        "Patient" => await PatientAsync(),
        "LabStaffWithout2FA" => await NoMfaLabStaffAsync(),
        _ => factory.CreatePortalClient(),
    };

    private async Task<HttpClient> PatientAsync()
    {
        var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(PortalFactory.PrimaryEmail, PortalFactory.Password);
        return client;
    }

    private async Task<HttpClient> NoMfaLabStaffAsync()
    {
        const string email = "lab.nomfa@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, email, false, "LabStaff");
        var client = factory.CreatePortalClient();
        Assert.Equal(HttpStatusCode.Redirect, (await client.SignInAsync(email, PortalFactory.Password)).StatusCode);
        return client;
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("LabStaff")]
    [InlineData("RadiologyStaff")]
    [InlineData("Patient")]
    [InlineData("LabStaffWithout2FA")]
    [InlineData("Anonymous")]
    public async Task Admin_area_access_matrix(string actor)
    {
        var client = await ClientFor(actor);
        var failures = new List<string>();
        foreach (var (family, paths) in Families)
        {
            var expected = actor switch
            {
                "Admin" => HttpStatusCode.OK,
                "Anonymous" => HttpStatusCode.Redirect,
                "LabStaff" or "RadiologyStaff" => StaffFamilies[actor].Contains(family) ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
                _ => HttpStatusCode.Forbidden,
            };
            foreach (var path in paths)
            {
                var response = await client.GetAsync(path);
                if (response.StatusCode != expected) failures.Add($"{actor} GET {path}: expected {(int)expected}, got {(int)response.StatusCode}");
                if (expected == HttpStatusCode.Redirect && response.Headers.Location?.ToString().Contains("/Identity/Account/Login") != true)
                    failures.Add($"{actor} GET {path}: expected login challenge");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData("LabStaff", "/Admin/Patients/Create")]
    [InlineData("RadiologyStaff", "/Admin/Patients/Create")]
    [InlineData("LabStaff", "/Admin/RadiologyStudies/Create")]
    [InlineData("RadiologyStaff", "/Admin/LabResults/Create")]
    [InlineData("LabStaff", "/Admin/StaffAccess/Update")]
    [InlineData("RadiologyStaff", "/Admin/Imports/Upload")]
    public async Task Staff_writes_outside_their_scope_are_forbidden(string actor, string path)
    {
        var client = await ClientFor(actor);
        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["x"] = "1" }));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Staff_dashboard_and_sidebar_show_only_permitted_modules()
    {
        var lab = await (await ClientFor("LabStaff")).GetStringAsync("/Admin");
        Assert.Contains("href=\"/Admin/LabResults\"", lab);
        foreach (var hidden in new[] { "href=\"/Admin/Patients\"", "href=\"/Admin/RadiologyStudies\"", "href=\"/Admin/Audit\"", "href=\"/Admin/Imports\"",
                     "href=\"/Admin/StaffAccess\"", "Recent staff activity", "Recent imports", "Unlinked patient records", "Radiology awaiting release" })
            Assert.DoesNotContain(hidden, lab);

        var radiology = await (await ClientFor("RadiologyStaff")).GetStringAsync("/Admin/RadiologyStudies");
        Assert.Contains("href=\"/Admin/RadiologyStudies\"", radiology);
        Assert.DoesNotContain("href=\"/Admin/LabResults\"", radiology);
        Assert.DoesNotContain("href=\"/Admin/Patients\"", radiology);

        var admin = await (await ClientFor("Admin")).GetStringAsync("/Admin");
        foreach (var shown in new[] { "href=\"/Admin/Patients\"", "href=\"/Admin/StaffAccess\"", "Recent staff activity", "Radiology awaiting release" })
            Assert.Contains(shown, admin);
    }

    [Fact]
    public async Task Patient_picker_shows_only_name_and_hospital_number()
    {
        var html = await (await ClientFor("RadiologyStaff")).GetStringAsync("/Admin/RadiologyStudies/Create");
        Assert.Contains("Hospital number", html);
        Assert.DoesNotContain(PortalFactory.PrimaryEmail, html);
        Assert.DoesNotContain("Linked", html);
        Assert.DoesNotContain("Unlinked", html);
    }

    [Fact]
    public async Task Portal_shows_administration_link_to_staff_roles_only()
    {
        Assert.Contains("href=\"/Admin\"", await (await ClientFor("LabStaff")).GetStringAsync("/Patient/Home"));
        Assert.Contains("href=\"/Admin\"", await (await ClientFor("RadiologyStaff")).GetStringAsync("/Patient/Home"));
        Assert.DoesNotContain("href=\"/Admin\"", await (await ClientFor("Patient")).GetStringAsync("/Patient/Home"));
    }

    [Fact]
    public async Task Staff_roles_exist_after_startup()
    {
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "LabStaff", "RadiologyStaff" }) Assert.True(await roles.RoleExistsAsync(role), role);
    }

    private static Endpoint AdminEndpoint(string controller, params object[] metadata)
    {
        var action = new ControllerActionDescriptor { ControllerName = controller, ActionName = "Index", RouteValues = new Dictionary<string, string?> { ["area"] = "Admin", ["controller"] = controller } };
        return new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection([action, .. metadata]), controller);
    }

    [Fact]
    public void Startup_guard_rejects_unmapped_anonymous_or_unknown_policy_admin_endpoints()
    {
        AdminEndpointGuard.Validate([AdminEndpoint("NewModule", new AuthorizeAttribute("AdminAccess"))]);
        AdminEndpointGuard.Validate([AdminEndpoint("LabResults", new AuthorizeAttribute("LabStaffAccess"))]);
        Assert.Equal("AdminAccess", AdminPolicies.For("NewModule"));

        Assert.Throws<InvalidOperationException>(() => AdminEndpointGuard.Validate([AdminEndpoint("NewModule")]));
        Assert.Throws<InvalidOperationException>(() => AdminEndpointGuard.Validate([AdminEndpoint("NewModule", new AuthorizeAttribute())]));
        Assert.Throws<InvalidOperationException>(() => AdminEndpointGuard.Validate([AdminEndpoint("NewModule", new AuthorizeAttribute("AdminAccess"), new AllowAnonymousAttribute())]));
        Assert.Throws<InvalidOperationException>(() => AdminEndpointGuard.Validate([AdminEndpoint("NewModule", new AuthorizeAttribute("SomethingElse"))]));
        // A staff policy on a controller not mapped to it fails closed.
        Assert.Throws<InvalidOperationException>(() => AdminEndpointGuard.Validate([AdminEndpoint("Patients", new AuthorizeAttribute("LabStaffAccess"))]));
        Assert.Throws<InvalidOperationException>(() => AdminEndpointGuard.Validate([AdminEndpoint("RadiologyStudies", new AuthorizeAttribute("LabStaffAccess"))]));
    }

    [Fact]
    public async Task Staff_access_screen_grants_and_revokes_only_staff_roles_with_audit()
    {
        const string email = "candidate.staff@fixtures.test";
        const string ineligible = "candidate.nomfa@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, ineligible, false);
        var candidate = await StaffAccounts.SignInAsync(factory, email);
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.GetAsync("/Admin/LabResults")).StatusCode);
        var admin = await StaffAccounts.AdminAsync(factory);

        async Task<(string Id, string Stamp, string Security)> Account(string address)
        {
            using var scope = factory.Services.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().SingleAsync(u => u.Email == address);
            return (user.Id, user.ConcurrencyStamp!, user.SecurityStamp!);
        }
        Task<HttpResponseMessage> Update(string id, string role, bool grant, string stamp) =>
            admin.PostFormAsync("/Admin/StaffAccess?all=true", "/Admin/StaffAccess/Update", new Dictionary<string, string>
            { ["userId"] = id, ["role"] = role, ["grant"] = grant ? "true" : "false", ["concurrencyStamp"] = stamp, ["all"] = "true" });

        var before = await Account(email);
        Assert.Equal(HttpStatusCode.BadRequest, (await Update(before.Id, "Admin", true, before.Stamp)).StatusCode);
        var granted = await Update(before.Id, "LabStaff", true, before.Stamp);
        Assert.Equal(HttpStatusCode.Redirect, granted.StatusCode);
        var afterGrant = await Account(email);
        Assert.NotEqual(before.Security, afterGrant.Security);
        Assert.Equal(HttpStatusCode.OK, (await candidate.GetAsync("/Admin/LabResults")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.GetAsync("/Admin/Patients")).StatusCode);

        // A grant or revoke prepared against the earlier account state is rejected.
        await Update(before.Id, "RadiologyStaff", true, before.Stamp);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            Assert.False(await users.IsInRoleAsync(user, "RadiologyStaff"));
            Assert.False(await users.IsInRoleAsync(user, "Admin"));
            Assert.True(await users.IsInRoleAsync(user, "LabStaff"));
        }

        var other = await Account(ineligible);
        await Update(other.Id, "LabStaff", true, other.Stamp);
        Assert.Contains("two-factor authentication enabled", await admin.GetStringAsync("/Admin/StaffAccess?all=true"));
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.False(await users.IsInRoleAsync((await users.FindByEmailAsync(ineligible))!, "LabStaff"));
        }

        var revoked = await Update(before.Id, "LabStaff", false, afterGrant.Stamp);
        Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.GetAsync("/Admin/LabResults")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var audit = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AdminAuditLogs
                .Where(a => a.Entity == "StaffAccess" && a.RecordKey == before.Id).ToListAsync();
            Assert.Contains(audit, a => a.Action == "Grant" && a.ChangedFields == "[\"LabStaff\"]");
            Assert.Contains(audit, a => a.Action == "Revoke" && a.ChangedFields == "[\"LabStaff\"]");
            Assert.Equal(2, audit.Count);
        }
    }
}
