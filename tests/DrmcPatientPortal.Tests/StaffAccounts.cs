using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DrmcPatientPortal.Tests;

// Creates email-confirmed, 2FA-enabled synthetic staff accounts and signs them in with an
// authenticator code so the session carries amr=mfa, as the staff policies require.
internal static partial class StaffAccounts
{
    public const string AdminEmail = "admin.staff@fixtures.test";
    public const string LabEmail = "lab.staff@fixtures.test";
    public const string RadiologyEmail = "radiology.staff@fixtures.test";
    public const string PatientServicesEmail = "pacd.staff@fixtures.test";

    private static readonly ConcurrentDictionary<(object, string), Task<HttpClient>> Sessions = new();

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    public static string Token(string html)
    {
        var match = AntiforgeryToken().Match(html);
        Assert.True(match.Success, "Page did not render an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static Task<HttpClient> AdminAsync(PortalFactory factory) => SignInAsync(factory, AdminEmail, "Admin");
    public static Task<HttpClient> LabAsync(PortalFactory factory) => SignInAsync(factory, LabEmail, "LabStaff");
    public static Task<HttpClient> RadiologyAsync(PortalFactory factory) => SignInAsync(factory, RadiologyEmail, "RadiologyStaff");
    public static Task<HttpClient> PatientServicesAsync(PortalFactory factory) => SignInAsync(factory, PatientServicesEmail, "PatientServicesStaff");

    // Sessions are cached per host so one authenticator code is never replayed for the same account.
    public static Task<HttpClient> SignInAsync<T>(WebApplicationFactory<T> host, string email, params string[] roles) where T : class =>
        Sessions.GetOrAdd((host, email), _ => CreateSessionAsync(host, email, roles));

    public static async Task<string> EnsureAccountAsync(IServiceProvider services, string email, bool twoFactor = true, params string[] roles)
    {
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FirstName = "Synthetic", LastName = "Staff", FullName = "Synthetic Staff" };
            var created = await users.CreateAsync(user, PortalFactory.Password);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
            if (twoFactor)
            {
                await users.ResetAuthenticatorKeyAsync(user);
                await users.SetTwoFactorEnabledAsync(user, true);
            }
        }
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role)) await roleManager.CreateAsync(new IdentityRole(role));
            if (!await users.IsInRoleAsync(user, role)) await users.AddToRoleAsync(user, role);
        }
        return (await users.GetAuthenticatorKeyAsync(user)) ?? "";
    }

    private static async Task<HttpClient> CreateSessionAsync<T>(WebApplicationFactory<T> host, string email, string[] roles) where T : class
    {
        var key = await EnsureAccountAsync(host.Services, email, true, roles);
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var login = await client.SignInAsync(email, PortalFactory.Password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Contains("LoginWith2fa", login.Headers.Location?.OriginalString);
        var page = await client.GetStringAsync(login.Headers.Location!.OriginalString);
        var verify = await client.PostAsync(login.Headers.Location.OriginalString, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.TwoFactorCode"] = Totp(key),
            ["Input.RememberMachine"] = "false",
            ["__RequestVerificationToken"] = Token(page),
        }));
        Assert.Equal(HttpStatusCode.Redirect, verify.StatusCode);
        return client;
    }

    public static string Totp(string base32)
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

    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string formPage, string action, IDictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(formPage);
        var body = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = Token(page) };
        return await client.PostAsync(action, new FormUrlEncodedContent(body));
    }
}
