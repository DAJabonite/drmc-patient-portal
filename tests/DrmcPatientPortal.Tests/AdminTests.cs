using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed partial class AdminTests(PortalFactory factory)
{
    private const string AdminEmail = "admin.staff@fixtures.test";

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

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

    private async Task<HttpClient> SignInAdminAsync()
    {
        string key;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roles.RoleExistsAsync("Admin")) await roles.CreateAsync(new IdentityRole("Admin"));
            var user = await users.FindByEmailAsync(AdminEmail);
            if (user is null)
            {
                user = new ApplicationUser { UserName = AdminEmail, Email = AdminEmail, EmailConfirmed = true, FirstName = "Ada", LastName = "Staff", FullName = "Ada Staff" };
                var created = await users.CreateAsync(user, PortalFactory.Password);
                Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
                await users.ResetAuthenticatorKeyAsync(user);
                await users.SetTwoFactorEnabledAsync(user, true);
                await users.AddToRoleAsync(user, "Admin");
            }
            key = (await users.GetAuthenticatorKeyAsync(user))!;
        }

        var client = factory.CreatePortalClient();
        var login = await client.SignInAsync(AdminEmail, PortalFactory.Password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Contains("LoginWith2fa", login.Headers.Location?.OriginalString);

        var page = await client.GetStringAsync(login.Headers.Location!.OriginalString);
        var token = AntiforgeryToken().Match(page);
        Assert.True(token.Success, "Two-factor page did not render an antiforgery token.");
        var verify = await client.PostAsync(login.Headers.Location.OriginalString, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.TwoFactorCode"] = Totp(key),
            ["Input.RememberMachine"] = "false",
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
        }));
        Assert.Equal(HttpStatusCode.Redirect, verify.StatusCode);
        return client;
    }

    private static string Totp(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(base32.TrimEnd('=').ToUpperInvariant().Select(c => Convert.ToString(alphabet.IndexOf(c), 2).PadLeft(5, '0')));
        var secret = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0f;
        var code = ((hash[offset] & 0x7f) << 24 | hash[offset + 1] << 16 | hash[offset + 2] << 8 | hash[offset + 3]) % 1_000_000;
        return code.ToString("D6");
    }
}
