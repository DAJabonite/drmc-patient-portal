using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Tests;

internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class TestEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "DrmcPatientPortal";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}

public sealed class SecurityConfigurationTests
{
    [Fact]
    public void Authenticator_signin_matches_the_rfc6238_sha1_test_vector()
    {
        // RFC 6238 Appendix B: SHA-1 secret "12345678901234567890", time 59, 8-digit value 94287082.
        Assert.Equal("287082", SingleUseAuthenticatorProvider.Code("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", 59 / 30));
    }
    [Theory]
    [InlineData("", "portal.example.org", false)]
    [InlineData("http://portal.example.org", "portal.example.org", false)]
    [InlineData("https://portal.example.org/path", "portal.example.org", false)]
    [InlineData("https://portal.example.org", "*", false)]
    [InlineData("https://portal.example.org", "*.example.org", false)]
    [InlineData("https://portal.example.org", "other.example.org", false)]
    [InlineData("https://portal.example.org", "portal.example.org;other.example.org", true)]
    public void Production_requires_a_canonical_https_origin_and_matching_explicit_hosts(string origin, string hosts, bool valid)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = hosts }).Build();
        var result = new PortalOptionsValidator(new TestEnvironment(), config).Validate(null, new() { PublicOrigin = origin });
        Assert.Equal(valid, result.Succeeded);
    }

    [Fact]
    public void Rate_limits_share_normalized_account_keys_without_looking_up_accounts_and_never_queue()
    {
        using var limiter = new PortalRequestLimiter(Options.Create(new PortalRateLimitOptions { AuthenticationPerAccount = 1, ConcurrentScans = 2 }));
        using var first = limiter.AcquireAccount(PortalRequestKind.Authentication, " missing@example.org ", "address");
        using var second = limiter.AcquireAccount(PortalRequestKind.Authentication, "MISSING@example.org", "address");
        using var otherAddress = limiter.AcquireAccount(PortalRequestKind.Authentication, "missing@example.org", "another-address");
        Assert.True(first.IsAcquired); Assert.False(second.IsAcquired); Assert.True(otherAddress.IsAcquired);
        using var scan1 = limiter.ScanConcurrency.AttemptAcquire();
        using var scan2 = limiter.ScanConcurrency.AttemptAcquire();
        using var scan3 = limiter.ScanConcurrency.AttemptAcquire();
        Assert.True(scan1.IsAcquired); Assert.True(scan2.IsAcquired); Assert.False(scan3.IsAcquired);
        scan1.Dispose();
        using var available = limiter.ScanConcurrency.AttemptAcquire(); Assert.True(available.IsAcquired);
    }

    [Theory]
    [InlineData("/Identity/Account/Login/")]
    [InlineData("/identity/account/login")]
    public void Authentication_routes_cannot_bypass_limits_with_casing_or_trailing_slashes(string path)
    {
        var context = new DefaultHttpContext(); context.Request.Path = path; context.Request.Method = "POST";
        Assert.Equal(PortalRequestKind.Authentication, PortalRequestControls.Kind(context.Request));
    }
}

[Collection(PortalCollection.Name)]
public sealed class SecurityHardeningTests(PortalFactory factory)
{
    private async Task<(string Id, string Email)> Account(bool mfa = false)
    {
        var email = $"security.{Guid.NewGuid():N}@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, email, mfa);
        using var scope = factory.Services.CreateScope();
        return ((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id, email);
    }
    private static string Cookie(HttpResponseMessage response) => Assert.Single(response.Headers.GetValues("Set-Cookie"),
        value => value.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal)).Split(';')[0];
    private HttpClient Replay(string cookie)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", cookie); return client;
    }
    private async Task<T> Read<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope(); return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    [Theory]
    [InlineData("", "localhost")]
    [InlineData("https://localhost", "*")]
    public void Incomplete_production_host_configuration_prevents_the_app_from_starting(string origin, string hosts)
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production"); b.UseSetting("Portal:PublicOrigin", origin); b.UseSetting("AllowedHosts", hosts);
            b.UseSetting("Notifications:Email:Host", "smtp.invalid"); b.UseSetting("Notifications:Email:FromAddress", "portal@example.org");
            b.UseSetting("Notifications:Sms:Endpoint", "https://sms.invalid/send"); b.UseSetting("Notifications:Sms:ApiKey", "synthetic-test-key");
        });
        var error = Assert.Throws<OptionsValidationException>(() => host.CreateClient());
        Assert.Contains(error.Failures, failure => failure.Contains("Portal:PublicOrigin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Signout_revokes_a_copied_cookie_but_keeps_an_independent_browser_signed_in()
    {
        var account = await Account(); using var first = factory.CreatePortalClient(); using var second = factory.CreatePortalClient();
        var cookie = Cookie(await first.SignInAsync(account.Email, PortalFactory.Password));
        await second.SignInSuccessfullyAsync(account.Email, PortalFactory.Password);
        using var copied = Replay(cookie); Assert.Equal(HttpStatusCode.OK, (await copied.GetAsync("/Patient/Home")).StatusCode);
        var logout = await first.PostFormAsync("/Patient/Home", "/Identity/Account/Logout", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await copied.GetAsync("/Patient/Home")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/Patient/Home")).StatusCode);
        Assert.Equal(1, await Read(db => db.PortalSessions.CountAsync(s => s.UserId == account.Id && s.RevokedUtc != null)));
    }

    [Fact]
    public async Task Password_change_rotates_the_current_session_and_invalidates_other_cookies_immediately()
    {
        var account = await Account(); using var current = factory.CreatePortalClient(); using var other = factory.CreatePortalClient();
        var oldCookie = Cookie(await current.SignInAsync(account.Email, PortalFactory.Password));
        await other.SignInSuccessfullyAsync(account.Email, PortalFactory.Password);
        var changed = await current.PostFormAsync("/Identity/Account/Manage/ChangePassword", "/Identity/Account/Manage/ChangePassword", new Dictionary<string, string>
        { ["Input.OldPassword"] = PortalFactory.Password, ["Input.NewPassword"] = "Changed#Pass2026", ["Input.ConfirmPassword"] = "Changed#Pass2026" });
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode); Assert.NotEqual(oldCookie, Cookie(changed));
        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/Patient/Home")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await other.GetAsync("/Patient/Home")).StatusCode);
        using var copied = Replay(oldCookie); Assert.Equal(HttpStatusCode.Redirect, (await copied.GetAsync("/Patient/Home")).StatusCode);
    }

    [Fact]
    public async Task Password_reset_invalidates_all_existing_sessions_on_their_next_request()
    {
        var account = await Account(); using var browser = factory.CreatePortalClient();
        await browser.SignInSuccessfullyAsync(account.Email, PortalFactory.Password);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await users.FindByIdAsync(account.Id))!;
            Assert.True((await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), "Reset#Pass2026")).Succeeded);
        }
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Patient/Home")).StatusCode);
    }

    [Fact]
    public async Task Legacy_cookies_without_a_session_identifier_require_a_fresh_signin()
    {
        var account = await Account(); using var browser = factory.CreatePortalClient();
        var cookie = Cookie(await browser.SignInAsync(account.Email, PortalFactory.Password));
        var format = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme).TicketDataFormat;
        var ticket = format.Unprotect(cookie[(cookie.IndexOf('=') + 1)..])!;
        var identity = (ClaimsIdentity)ticket.Principal.Identity!; identity.RemoveClaim(identity.FindFirst(PortalCookieEvents.SessionClaim)!);
        using var legacy = Replay(".AspNetCore.Identity.Application=" + format.Protect(ticket));
        Assert.Equal(HttpStatusCode.Redirect, (await legacy.GetAsync("/Patient/Home")).StatusCode);
    }

    [Fact]
    public async Task Session_renewal_cannot_recreate_a_revoked_session_and_checks_owner_stamp_and_expiry()
    {
        var account = await Account(); using var scope = factory.Services.CreateScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(account.Id))!;
        var store = factory.Services.GetRequiredService<PortalSessionStore>();
        var id = await store.CreateAsync(user.Id, user.SecurityStamp!, DateTimeOffset.UtcNow.AddMinutes(1), null, default);
        var identity = new SessionIdentity(id, user.Id, user.SecurityStamp!);
        Assert.True(await store.IsActiveAsync(identity, default));
        Assert.False(await store.IsActiveAsync(identity with { UserId = "another-owner" }, default));
        Assert.False(await store.IsActiveAsync(identity with { Stamp = "old-stamp" }, default));
        await Task.WhenAll(store.ExtendAsync(identity, DateTimeOffset.UtcNow.AddHours(1), default), store.RevokeAsync(identity, default));
        Assert.False(await store.ExtendAsync(identity, DateTimeOffset.UtcNow.AddDays(1), default));
        Assert.Equal(1, await Read(db => db.PortalSessions.CountAsync(s => s.Id == id)));
        var expired = await store.CreateAsync(user.Id, user.SecurityStamp!, DateTimeOffset.UtcNow.AddSeconds(-1), null, default);
        Assert.False(await store.IsActiveAsync(identity with { SessionId = expired }, default));
    }

    [Fact]
    public async Task Password_refresh_and_periodic_principal_refresh_preserve_verified_mfa()
    {
        var account = await Account(true); using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero)));
        var client = await StaffAccounts.SignInAsync(host, account.Email);
        await client.GetAsync("/Patient/Home");
        var changed = await client.PostFormAsync("/Identity/Account/Manage/ChangePassword", "/Identity/Account/Manage/ChangePassword", new Dictionary<string, string>
        { ["Input.OldPassword"] = PortalFactory.Password, ["Input.NewPassword"] = "MfaChanged#2026", ["Input.ConfirmPassword"] = "MfaChanged#2026" });
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        var format = host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme).TicketDataFormat;
        var cookie = Cookie(changed); var ticket = format.Unprotect(cookie[(cookie.IndexOf('=') + 1)..])!;
        Assert.True(ticket.Principal.HasClaim("amr", "mfa")); Assert.True(ticket.Principal.HasClaim(c => c.Type == PortalCookieEvents.SessionClaim));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Patient/Home")).StatusCode);
    }

    [Fact]
    public async Task Authenticator_codes_are_atomic_single_use_with_clock_tolerance_and_key_replacement()
    {
        var account = await Account(true); var clock = new TestClock(DateTimeOffset.FromUnixTimeSeconds(1800000000));
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => { s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(clock); }));
        async Task<(bool Valid, string Key)> Verify(string? code, bool replace = false)
        {
            using var scope = host.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(account.Id))!; if (replace) await users.ResetAuthenticatorKeyAsync(user);
            var key = (await users.GetAuthenticatorKeyAsync(user))!;
            return (await users.VerifyTwoFactorTokenAsync(user, SingleUseAuthenticatorProvider.ProviderName, code ?? SingleUseAuthenticatorProvider.Code(key, clock.Now.ToUnixTimeSeconds() / 30)), key);
        }
        using var initialScope = factory.Services.CreateScope();
        var initialUsers = initialScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var key = (await initialUsers.GetAuthenticatorKeyAsync((await initialUsers.FindByIdAsync(account.Id))!))!;
        var step = clock.Now.ToUnixTimeSeconds() / 30; var code = SingleUseAuthenticatorProvider.Code(key, step);
        var simultaneous = await Task.WhenAll(Verify(code), Verify(code)); Assert.Single(simultaneous, r => r.Valid);
        Assert.True((await Verify(SingleUseAuthenticatorProvider.Code(key, step - 2))).Valid);
        Assert.True((await Verify(SingleUseAuthenticatorProvider.Code(key, step + 2))).Valid);
        Assert.False((await Verify(code)).Valid);
        clock.Now = clock.Now.AddSeconds(30); Assert.True((await Verify(null)).Valid);
        var replaced = await Verify(null, true); Assert.True(replaced.Valid); Assert.NotEqual(key, replaced.Key);
        Assert.False((await Verify(code)).Valid);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("wrong")]
    [InlineData("locked")]
    [InlineData("unconfirmed")]
    public async Task Failed_signins_have_generic_feedback_and_a_minimum_duration(string state)
    {
        var account = await Account();
        if (state is "locked" or "unconfirmed")
        {
            using var scope = factory.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(account.Id))!;
            if (state == "locked") await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(15));
            else { user.EmailConfirmed = false; await users.UpdateAsync(user); }
        }
        using var client = factory.CreatePortalClient(); var watch = Stopwatch.StartNew();
        var response = await client.SignInAsync(state == "unknown" ? $"missing.{Guid.NewGuid():N}@fixtures.test" : account.Email,
            state == "wrong" ? "Wrong#Password" : PortalFactory.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Contains("Invalid login attempt.", await response.Content.ReadAsStringAsync());
        Assert.True(watch.ElapsedMilliseconds >= 245); Assert.DoesNotContain("/Lockout", response.Headers.Location?.OriginalString ?? "");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Email_requests_are_generic_delayed_and_throttled_for_existing_and_unknown_accounts(bool existing)
    {
        var account = await Account(); using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.Configure<PortalRateLimitOptions>(o => o.EmailPerAccount = 1)));
        using var client = host.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        var fields = new Dictionary<string, string> { ["Input.Email"] = existing ? account.Email : $"unknown.{Guid.NewGuid():N}@fixtures.test" };
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.1");
        var watch = Stopwatch.StartNew(); var response = await client.PostFormAsync("/Identity/Account/ForgotPassword", "/Identity/Account/ForgotPassword", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.True(watch.ElapsedMilliseconds >= 245);
        Assert.Equal("/Identity/Account/ForgotPasswordConfirmation", response.Headers.Location?.OriginalString);
        client.DefaultRequestHeaders.Remove("X-Forwarded-For"); client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.2");
        var limited = await client.PostFormAsync("/Identity/Account/ForgotPassword", "/Identity/Account/ForgotPassword", fields);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode); Assert.NotNull(limited.Headers.RetryAfter);
    }

    [Fact]
    public async Task Untrusted_hosts_are_rejected_and_forwarded_headers_cannot_change_email_origins()
    {
        var account = await Account(); using var host = factory.WithWebHostBuilder(b => b.UseSetting("Portal:PublicOrigin", "https://portal.example.org"));
        using var client = host.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var forged = new HttpRequestMessage(HttpMethod.Get, "/Identity/Account/ForgotPassword"); forged.Headers.Host = "outside.invalid";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(forged)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "outside.invalid");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.99");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");
        var reset = await client.PostFormAsync("/Identity/Account/ForgotPassword", "/Identity/Account/ForgotPassword", new Dictionary<string, string> { ["Input.Email"] = account.Email });
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        using var scope = host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var queued = await db.AccountEmails.SingleAsync(m => m.UserId == account.Id);
        var protector = host.Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>().CreateProtector("DRMC.AccountEmail.v1");
        var body = Encoding.UTF8.GetString(protector.Unprotect(queued.ProtectedPayload!));
        Assert.Contains("https://portal.example.org/Identity/Account/ResetPassword", body); Assert.DoesNotContain("outside.invalid", body);
        var links = host.Services.GetRequiredService<AccountLinks>(); var context = new DefaultHttpContext { RequestServices = host.Services };
        context.Request.Scheme = "http"; context.Request.Host = new("outside.invalid");
        foreach (var page in new[] { "/Account/Register", "/Account/ConfirmEmail", "/Account/ConfirmEmailChange", "/Account/ResetPassword" })
            Assert.StartsWith("https://portal.example.org/Identity/", links.Page(context, page, new { area = "Identity", userId = "user", code = "code" }));
    }

    [Fact]
    public async Task Decoded_nul_form_text_returns_a_clear_bad_request()
    {
        using var client = factory.CreatePortalClient(); var response = await client.PostAsync("/Home/SetLanguage", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["culture"] = "en\0", ["returnUrl"] = "/" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Contains("Remove null characters", await response.Content.ReadAsStringAsync());
    }
}
