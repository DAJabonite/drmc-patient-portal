using System.Net;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed class AdminTests(PortalFactory factory)
{
    [Fact]
    public async Task Admin_requires_sign_in()
    {
        var response = await factory.CreatePortalClient().GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Admin_console_pages_render_in_admin_layout()
    {
        var client = await SignInAdminAsync();
        string[] paths =
        [
            "/Admin", "/Admin/Patients", "/Admin/Patients/Create", "/Admin/ClinicalEncounters", "/Admin/LabResults",
            "/Admin/LabResultItems", "/Admin/Prescriptions", "/Admin/MedicationDoseSchedules", "/Admin/PatientAllergies",
            "/Admin/Doctors", "/Admin/Doctors/Create", "/Admin/PublicAdvisories", "/Admin/Imports", "/Admin/Audit",
        ];
        foreach (var path in paths)
        {
            var response = await client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} returned {(int)response.StatusCode}");
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("admin-sidebar", html);
            Assert.DoesNotContain("<script>", html);
        }

        var patients = await client.GetStringAsync("/Admin/Patients");
        var details = PortalClient.Links(patients, "/Admin/Patients/Details/");
        Assert.NotEmpty(details);
        foreach (var path in new[] { details[0], details[0].Replace("Details", "Edit"), details[0].Replace("Details", "Delete"), details[0].Replace("Details", "Link") })
        {
            var response = await client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} returned {(int)response.StatusCode}");
            Assert.Contains("admin-card", await response.Content.ReadAsStringAsync());
        }
    }

    private Task<HttpClient> SignInAdminAsync() => StaffAccounts.AdminAsync(factory);
}
