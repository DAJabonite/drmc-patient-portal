using System.Net;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed partial class RecoveryCodeTests(PortalFactory factory)
{
    private const string Settings = "/Identity/Account/Manage/TwoFactorAuthentication";
    private const string Enable = "/Identity/Account/Manage/EnableAuthenticator";
    private const string Generate = "/Identity/Account/Manage/GenerateRecoveryCodes";
    private const string Show = "/Identity/Account/Manage/ShowRecoveryCodes";

    [GeneratedRegex("data-recovery-code[^>]*>([^<]+)</code>")]
    private static partial Regex CodeElements();

    private static string[] Codes(string html) => CodeElements().Matches(html)
        .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToArray();

    private async Task<(HttpClient Client, string Email)> AccountAsync(bool staff = false)
    {
        var email = $"recovery.{Guid.NewGuid():N}@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, email, false, staff ? ["LabStaff"] : []);
        var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email, PortalFactory.Password);
        return (client, email);
    }

    private async Task<string> KeyAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.GetAuthenticatorKeyAsync((await users.FindByEmailAsync(email))!))!;
    }

    private async Task<int> RemainingAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.CountRecoveryCodesAsync((await users.FindByEmailAsync(email))!);
    }

    private async Task<bool> RedeemAsync(string email, string code)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.RedeemTwoFactorRecoveryCodeAsync((await users.FindByEmailAsync(email))!, code)).Succeeded;
    }

    private async Task<HttpResponseMessage> VerifyAsync(HttpClient client, string email)
    {
        var html = await client.GetStringAsync(Enable);
        return await client.PostAsync(Enable, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Code"] = StaffAccounts.Totp(await KeyAsync(email)),
            ["__RequestVerificationToken"] = StaffAccounts.Token(html),
        }));
    }

    private async Task<string[]> SetUpAsync(HttpClient client, string email)
    {
        var enabled = await VerifyAsync(client, email);
        Assert.Equal(HttpStatusCode.Redirect, enabled.StatusCode);
        Assert.Equal(Show, enabled.Headers.Location?.OriginalString);
        var shown = await client.GetAsync(Show);
        var html = await shown.Content.ReadAsStringAsync();
        Assert.True(shown.IsSuccessStatusCode, $"Recovery code page returned {(int)shown.StatusCode}: {html[..Math.Min(2000, html.Length)]}");
        Assert.Contains("no-store", shown.Headers.CacheControl?.ToString());
        var codes = Codes(html);
        Assert.Equal(10, codes.Length);
        Assert.Equal(10, codes.Distinct().Count());
        return codes;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task First_setup_shows_recovery_codes_once_and_links_them_from_settings(bool staff)
    {
        var (client, email) = await AccountAsync(staff);
        using (client)
        {
            await SetUpAsync(client, email);
            Assert.Equal(10, await RemainingAsync(email));

            var revisited = await client.GetAsync(Show);
            Assert.Equal(HttpStatusCode.Redirect, revisited.StatusCode);
            Assert.Equal(Settings, revisited.Headers.Location?.OriginalString);

            var settings = await client.GetStringAsync(Settings);
            Assert.Contains($"href=\"{Generate}\"", settings);
            Assert.Contains("10 recovery codes remaining.", settings);
        }
    }

    [Fact]
    public async Task Reconfiguring_an_authenticator_preserves_existing_recovery_codes()
    {
        var (client, email) = await AccountAsync();
        using (client)
        {
            var codes = await SetUpAsync(client, email);
            var key = await KeyAsync(email);
            var configured = await VerifyAsync(client, email);
            Assert.Equal(HttpStatusCode.Redirect, configured.StatusCode);
            Assert.Equal(Settings, configured.Headers.Location?.OriginalString);
            Assert.Equal(key, await KeyAsync(email));
            Assert.Equal(10, await RemainingAsync(email));
            Assert.True(await RedeemAsync(email, codes[0]));
        }
    }

    [Fact]
    public async Task Regeneration_requires_a_form_post_and_replaces_codes_without_changing_the_authenticator()
    {
        var (client, email) = await AccountAsync();
        using (client)
        {
            var previous = await SetUpAsync(client, email);
            var key = await KeyAsync(email);
            var confirmation = await client.GetAsync(Generate);
            Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
            Assert.Contains("no-store", confirmation.Headers.CacheControl?.ToString());
            Assert.Contains("replaces all your previous recovery codes", await confirmation.Content.ReadAsStringAsync());
            Assert.Equal(10, await RemainingAsync(email));

            var missingToken = await client.PostAsync(Generate, new FormUrlEncodedContent(new Dictionary<string, string>()));
            Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
            Assert.Equal(10, await RemainingAsync(email));

            var generated = await client.PostFormAsync(Generate, Generate, new Dictionary<string, string>());
            Assert.Equal(HttpStatusCode.Redirect, generated.StatusCode);
            Assert.Equal(Show, generated.Headers.Location?.OriginalString);
            var replacements = Codes(await client.GetStringAsync(Show));
            Assert.Equal(10, replacements.Length);
            Assert.Equal(10, await RemainingAsync(email));
            Assert.Equal(key, await KeyAsync(email));
            Assert.False(await RedeemAsync(email, previous[0]));
            Assert.True(await RedeemAsync(email, replacements[0]));
            Assert.Equal(9, await RemainingAsync(email));
        }
    }

    [Fact]
    public async Task Recovery_code_signin_works_once_and_returns_staff_to_administration()
    {
        var (client, email) = await AccountAsync(staff: true);
        using (client)
        {
            var codes = await SetUpAsync(client, email);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var login = factory.CreatePortalClient();
                const string loginPage = "/Identity/Account/Login?returnUrl=%2FAdmin";
                var password = await login.PostFormAsync(loginPage, loginPage, new Dictionary<string, string>
                {
                    ["Input.Email"] = email,
                    ["Input.Password"] = PortalFactory.Password,
                    ["Input.RememberMe"] = "false",
                });
                Assert.Equal(HttpStatusCode.Redirect, password.StatusCode);
                var challenge = await login.GetStringAsync(password.Headers.Location!.OriginalString);
                var link = Regex.Match(challenge, "href=\"([^\"]*LoginWithRecoveryCode[^\"]*)\"");
                Assert.True(link.Success, "The authenticator challenge must link to recovery-code sign-in.");
                var path = WebUtility.HtmlDecode(link.Groups[1].Value);
                Assert.Contains("returnUrl=%2FAdmin", path);
                var recovered = await login.PostFormAsync(path, path, new Dictionary<string, string> { ["Input.RecoveryCode"] = codes[0] });
                if (attempt == 0)
                {
                    Assert.Equal(HttpStatusCode.Redirect, recovered.StatusCode);
                    Assert.Equal("/Admin", recovered.Headers.Location?.OriginalString);
                    Assert.Equal(HttpStatusCode.OK, (await login.GetAsync("/Admin")).StatusCode);
                }
                else
                {
                    Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
                    Assert.Contains("Invalid recovery code", await recovered.Content.ReadAsStringAsync());
                    Assert.Equal(HttpStatusCode.Redirect, (await login.GetAsync("/Admin")).StatusCode);
                }
            }
            Assert.Equal(9, await RemainingAsync(email));
        }
    }

    [Fact]
    public async Task Accounts_without_two_factor_cannot_generate_recovery_codes()
    {
        var (client, email) = await AccountAsync();
        using (client)
        {
            var settings = await client.GetStringAsync(Settings);
            Assert.DoesNotContain($"href=\"{Generate}\"", settings);
            foreach (var path in new[] { Generate, Show })
            {
                var page = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
                Assert.Equal(Settings, page.Headers.Location?.OriginalString);
            }
            var posted = await client.PostFormAsync(Settings, Generate, new Dictionary<string, string>());
            Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
            Assert.Equal(Settings, posted.Headers.Location?.OriginalString);
            Assert.Equal(0, await RemainingAsync(email));
        }
    }

    [Theory]
    [InlineData(Generate)]
    [InlineData(Show)]
    public async Task Recovery_pages_require_signin(string path)
    {
        using var client = factory.CreatePortalClient();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Pending_recovery_codes_are_not_shown_after_switching_accounts()
    {
        var (client, email) = await AccountAsync();
        using (client)
        {
            var pending = await VerifyAsync(client, email);
            Assert.Equal(Show, pending.Headers.Location?.OriginalString);
            var logout = await client.PostFormAsync(Settings, "/Identity/Account/Logout", new Dictionary<string, string>());
            Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);

            var otherEmail = $"recovery.other.{Guid.NewGuid():N}@fixtures.test";
            var key = await StaffAccounts.EnsureAccountAsync(factory.Services, otherEmail);
            var password = await client.SignInAsync(otherEmail, PortalFactory.Password);
            Assert.Contains("LoginWith2fa", password.Headers.Location?.OriginalString);
            var path = password.Headers.Location!.OriginalString;
            var signin = await client.PostFormAsync(path, path, new Dictionary<string, string>
            {
                ["Input.TwoFactorCode"] = StaffAccounts.Totp(key),
                ["Input.RememberMachine"] = "false",
            });
            Assert.Equal(HttpStatusCode.Redirect, signin.StatusCode);

            var shown = await client.GetAsync(Show);
            Assert.Equal(HttpStatusCode.Redirect, shown.StatusCode);
            Assert.Equal(Settings, shown.Headers.Location?.OriginalString);
            Assert.Empty(Codes(await shown.Content.ReadAsStringAsync()));
        }
    }
}
