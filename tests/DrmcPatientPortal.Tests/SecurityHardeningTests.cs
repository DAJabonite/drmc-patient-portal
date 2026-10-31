using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;
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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
    private static EmailDeliveryOptions SmtpOptions(string? provider = "Smtp") => new()
    {
        Provider = provider, Host = "smtp.gmail.com", Port = 587, UseSsl = true,
        Username = "portal@fixtures.test", Password = "synthetic-password", FromAddress = "portal@fixtures.test"
    };

    [Theory]
    [InlineData("Development", null, "Console")]
    [InlineData("Production", null, "Smtp")]
    [InlineData("Staging", "", "Smtp")]
    [InlineData("Development", "sMtP", "Smtp")]
    [InlineData("Development", " console ", "Console")]
    public void Email_provider_defaults_and_explicit_selection_are_validated(string environment, string? provider, string expected)
    {
        var options = SmtpOptions(provider);
        var validator = new EmailDeliveryOptionsValidator(new TestEnvironment { EnvironmentName = environment });
        Assert.Equal(expected, options.SelectedProvider(environment == "Development"), ignoreCase: true);
        Assert.True(validator.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("Production", "Console")]
    [InlineData("Staging", "cOnSoLe")]
    [InlineData("Development", "Gmail")]
    public void Unsafe_or_unknown_email_providers_are_rejected(string environment, string provider)
    {
        var validator = new EmailDeliveryOptionsValidator(new TestEnvironment { EnvironmentName = environment });
        Assert.True(validator.Validate(null, SmtpOptions(provider)).Failed);
    }

    [Theory]
    [InlineData("Host", "")]
    [InlineData("Port", "0")]
    [InlineData("Port", "65536")]
    [InlineData("Username", "")]
    [InlineData("Password", "")]
    [InlineData("Password", " \t\n ")]
    [InlineData("FromAddress", "invalid-address")]
    [InlineData("UseSsl", "false")]
    public void Incomplete_smtp_configuration_is_rejected_in_development_and_production(string key, string value)
    {
        var values = new Dictionary<string, string?>
        {
            ["Provider"] = "Smtp", ["Host"] = "smtp.gmail.com", ["Port"] = "587", ["UseSsl"] = "true",
            ["Username"] = "portal@fixtures.test", ["Password"] = "synthetic-password", ["FromAddress"] = "portal@fixtures.test"
        };
        values[key] = value;
        var options = new ConfigurationBuilder().AddInMemoryCollection(values).Build().Get<EmailDeliveryOptions>()!;
        foreach (var environment in new[] { Environments.Development, Environments.Production })
        {
            var result = new EmailDeliveryOptionsValidator(new TestEnvironment { EnvironmentName = environment }).Validate(null, options);
            Assert.True(result.Failed);
            Assert.DoesNotContain("synthetic-password", result.FailureMessage);
        }
    }

    [Theory]
    [InlineData("smtp.gmail.com", "abcd efgh ijkl mnop", "abcdefghijklmnop")]
    [InlineData("SMTP.GMAIL.COM", " abcd\tefgh\nijkl\u00a0mnop ", "abcdefghijklmnop")]
    [InlineData("smtp.example.org", " abcd efgh\tijkl mnop ", " abcd efgh\tijkl mnop ")]
    [InlineData("smtp.gmail.com.example.org", "abcd efgh ijkl mnop", "abcd efgh ijkl mnop")]
    public void App_password_grouping_is_removed_only_for_gmail(string host, string password, string expected)
    {
        var options = SmtpOptions();
        options.Host = host; options.Password = password;
        Assert.Equal(expected, options.AuthenticationPassword);
        Assert.Equal(password, options.Password);
    }

    [Fact]
    public async Task Smtp_message_has_real_plain_text_and_html_mime_alternatives_without_attachments()
    {
        const string text = "Invitation code: ABCDE-FGHIJ-KLMNO-PQRST\nKumpirmaha ang imong email.";
        const string html = "<p>Invitation code: ABCDE-FGHIJ-KLMNO-PQRST</p><p>Kumpirmaha ang imong email.</p>";
        var sender = new SmtpEmailSender(Options.Create(SmtpOptions()));
        using var message = sender.CreateMessage("recipient@fixtures.test", "Imbitasyon", html, text);
        Assert.False(message.IsBodyHtml);
        Assert.Equal(text, message.Body);
        Assert.Empty(message.Attachments);
        var alternative = Assert.Single(message.AlternateViews);
        Assert.Equal("text/html", alternative.ContentType.MediaType);
        using (var reader = new StreamReader(alternative.ContentStream, Encoding.UTF8, leaveOpen: true))
            Assert.Equal(html, await reader.ReadToEndAsync());
        alternative.ContentStream.Position = 0;

        var directory = Path.Combine(Path.GetTempPath(), "drmc-email-mime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Serialize the same message through .NET's SMTP implementation without opening a network connection.
            using var pickup = new SmtpClient { DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory, PickupDirectoryLocation = directory };
            await pickup.SendMailAsync(message);
            var raw = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory)));
            Assert.Contains("Content-Type: multipart/alternative", raw, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Content-Type: text/plain", raw, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Content-Type: text/html", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Content-Disposition: attachment", raw, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(directory, recursive: true); }

        using var legacy = sender.CreateMessage("recipient@fixtures.test", "Existing email", html, null);
        Assert.True(legacy.IsBodyHtml);
        Assert.Equal(html, legacy.Body);
        Assert.Empty(legacy.AlternateViews);
    }

    [Fact]
    public async Task Account_throttle_uses_the_first_email_value_when_login_fields_repeat()
    {
        using var limiter = new PortalRequestLimiter(Options.Create(new PortalRateLimitOptions { AuthenticationPerAccount = 1 }));
        var accepted = 0;
        var controls = new PortalRequestControls(_ => { accepted++; return Task.CompletedTask; });
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/Identity/Account/Login";
            context.Request.ContentType = "application/x-www-form-urlencoded";
            context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["Input.Email"] = new(new[] { "patient@fixtures.test", $"nonce{attempt}@fixtures.test" })
            });
            await controls.InvokeAsync(context, limiter);
            Assert.Equal(attempt == 0 ? 200 : 429, context.Response.StatusCode);
        }
        Assert.Equal(1, accepted);
    }

    [Fact]
    public void Disabled_mail_workers_are_not_required_for_readiness()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AccountEmail:WorkerEnabled"] = "false", ["Notifications:Outbox:WorkerEnabled"] = "false"
        }).Build();
        using var health = new PortalWorkerHealth(clock, config);
        clock.Now = clock.Now.AddMinutes(3);
        foreach (var worker in new[] { "imports", "security-cleanup", "temporary-documents", "operations" }) health.Success(worker);
        Assert.True(health.WorkersReady());
    }

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
    [InlineData("", typeof(ConsoleEmailSender))]
    [InlineData("cOnSoLe", typeof(ConsoleEmailSender))]
    [InlineData("sMtP", typeof(SmtpEmailSender))]
    public void Development_email_provider_selects_the_registered_transport(string provider, Type expected)
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Notifications:Email:Provider", provider);
            b.UseSetting("Notifications:Email:Host", "smtp.invalid"); b.UseSetting("Notifications:Email:FromAddress", "portal@fixtures.test");
            b.UseSetting("Notifications:Email:Username", "synthetic-user"); b.UseSetting("Notifications:Email:Password", "synthetic-password");
        });
        using var client = host.CreateClient();
        Assert.IsType(expected, host.Services.GetRequiredService<IEmailSender>());
    }

    [Theory]
    [InlineData("Smtp", "")]
    [InlineData("Unknown", "synthetic-password")]
    public void Invalid_development_email_configuration_prevents_startup(string provider, string password)
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Notifications:Email:Provider", provider);
            b.UseSetting("Notifications:Email:Host", "smtp.invalid"); b.UseSetting("Notifications:Email:FromAddress", "portal@fixtures.test");
            b.UseSetting("Notifications:Email:Username", "synthetic-user"); b.UseSetting("Notifications:Email:Password", password);
        });
        var error = Assert.Throws<OptionsValidationException>(() => host.CreateClient());
        Assert.Contains(error.Failures, failure => failure.Contains("SMTP", StringComparison.Ordinal) || failure.Contains("Provider", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "localhost")]
    [InlineData("https://localhost", "*")]
    public void Incomplete_production_host_configuration_prevents_the_app_from_starting(string origin, string hosts)
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production"); b.UseSetting("Portal:PublicOrigin", origin); b.UseSetting("AllowedHosts", hosts);
            b.UseSetting("Notifications:Email:Provider", "Smtp");
            b.UseSetting("Notifications:Email:Username", "synthetic-user"); b.UseSetting("Notifications:Email:Password", "synthetic-password");
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

    private static async Task<bool> VerifyTotp(IServiceProvider services, string userId, string code)
    {
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.VerifyTwoFactorTokenAsync((await users.FindByIdAsync(userId))!, SingleUseAuthenticatorProvider.ProviderName, code);
    }

    [Fact]
    public async Task Totp_replay_is_rejected_after_cache_eviction_host_replacement_and_a_cross_host_race()
    {
        var isolated = new PortalFactory();
        try
        {
            await isolated.InitializeAsync();
            var email = $"isolated.totp.{Guid.NewGuid():N}@fixtures.test";
            await StaffAccounts.EnsureAccountAsync(isolated.Services, email, true);
            string userId;
            using (var scope = isolated.Services.CreateScope())
                userId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
            async Task<T> ReadIsolated<T>(Func<ApplicationDbContext, Task<T>> query)
            {
                using var scope = isolated.Services.CreateScope();
                return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
            }
            var clock = new TestClock(DateTimeOffset.FromUnixTimeSeconds(1800000000));
            string key;
            using (var scope = isolated.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                key = (await users.GetAuthenticatorKeyAsync((await users.FindByIdAsync(userId))!))!;
            }
            WebApplicationFactory<Program> Host() => isolated.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            {
                s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(clock);
                s.RemoveAll<IMemoryCache>(); s.AddSingleton<IMemoryCache>(_ => new MemoryCache(new MemoryCacheOptions()));
            }));
            var step = clock.Now.ToUnixTimeSeconds() / 30;
            var code = SingleUseAuthenticatorProvider.Code(key, step);
            using (var first = Host())
            using (var second = Host())
            {
                Assert.True(await VerifyTotp(first.Services, userId, code));
                Assert.NotSame(first.Services.GetRequiredService<IMemoryCache>(), second.Services.GetRequiredService<IMemoryCache>());
                ((MemoryCache)first.Services.GetRequiredService<IMemoryCache>()).Compact(1);
                ((MemoryCache)second.Services.GetRequiredService<IMemoryCache>()).Compact(1);
                Assert.False(await VerifyTotp(second.Services, userId, code));
            }
            using var replacement = Host();
            Assert.False(await VerifyTotp(replacement.Services, userId, code));
            Assert.Equal(1, await ReadIsolated(db => db.AuthenticatorConsumptions.CountAsync(c => c.UserId == userId && c.TimeStep == step)));
            clock.Now = clock.Now.AddSeconds(30);
            using var competitor = Host();
            var next = SingleUseAuthenticatorProvider.Code(key, step + 1);
            var result = await Task.WhenAll(VerifyTotp(replacement.Services, userId, next), VerifyTotp(competitor.Services, userId, next));
            Assert.Single(result, valid => valid);
            Assert.Equal(1, await ReadIsolated(db => db.AuthenticatorConsumptions.CountAsync(c => c.UserId == userId && c.TimeStep == step + 1)));
            var cleanup = new PortalSecurityCleanup(replacement.Services.GetRequiredService<IServiceScopeFactory>(), clock);
            clock.Now = DateTimeOffset.FromUnixTimeSeconds((step + 3) * 30 - 1);
            await cleanup.CleanupAsync();
            Assert.False(await VerifyTotp(replacement.Services, userId, code));
            Assert.Equal(1, await ReadIsolated(db => db.AuthenticatorConsumptions.CountAsync(c => c.UserId == userId && c.TimeStep == step)));
            clock.Now = clock.Now.AddSeconds(1);
            await cleanup.CleanupAsync();
            Assert.False(await VerifyTotp(replacement.Services, userId, code));
            Assert.False(await ReadIsolated(db => db.AuthenticatorConsumptions.AnyAsync(c => c.UserId == userId && c.TimeStep == step)));
        }
        finally { await ((IAsyncLifetime)isolated).DisposeAsync(); }
    }


    [Theory]
    [InlineData("logout")]
    [InlineData("password-reset")]
    [InlineData("security-stamp")]
    public async Task Cookies_survive_host_replacement_and_revocation_is_immediate_on_other_hosts(string action)
    {
        var account = await Account();
        string cookie;
        using (var first = factory.WithWebHostBuilder(_ => { }))
        using (var browser = first.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false }))
            cookie = Cookie(await browser.SignInAsync(account.Email, PortalFactory.Password));
        using var second = factory.WithWebHostBuilder(_ => { });
        using var replacement = factory.WithWebHostBuilder(_ => { });
        HttpClient Client(WebApplicationFactory<Program> host, bool handleCookies = false)
        {
            var client = host.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false, HandleCookies = handleCookies });
            client.DefaultRequestHeaders.Add("Cookie", cookie);
            return client;
        }
        using var copied = Client(replacement);
        Assert.Equal(HttpStatusCode.OK, (await copied.GetAsync("/Patient/Home")).StatusCode);
        if (action == "logout")
        {
            using var browser = Client(second, true);
            Assert.Equal(HttpStatusCode.Redirect, (await browser.PostFormAsync("/Patient/Home", "/Identity/Account/Logout", new Dictionary<string, string>())).StatusCode);
        }
        else
        {
            using var scope = second.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(account.Id))!;
            var result = action == "password-reset"
                ? await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), "Reset#AcrossHosts2026")
                : await users.UpdateSecurityStampAsync(user);
            Assert.True(result.Succeeded);
        }
        Assert.Equal(HttpStatusCode.Redirect, (await copied.GetAsync("/Patient/Home")).StatusCode);
    }

    private sealed class RejectConsumptionInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuthenticatorConsumption>().Any(e => e.State == EntityState.Added))
                throw new TimeoutException("Synthetic unavailable replay store.");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task An_unavailable_replay_store_cannot_accept_a_token_or_fall_back_to_memory()
    {
        var account = await Account(true);
        string code;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var key = (await users.GetAuthenticatorKeyAsync((await users.FindByIdAsync(account.Id))!))!;
            code = SingleUseAuthenticatorProvider.Code(key, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        }
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<ApplicationDbContext>(o => o.AddInterceptors(new RejectConsumptionInsert()))));
        await Assert.ThrowsAsync<TimeoutException>(() => VerifyTotp(host.Services, account.Id, code));
        Assert.False(await Read(db => db.AuthenticatorConsumptions.AnyAsync(c => c.UserId == account.Id)));
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
