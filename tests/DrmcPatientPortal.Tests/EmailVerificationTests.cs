using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed class EmailVerificationTests(PortalFactory factory)
{
    private const string ResendPath = "/Identity/Account/ResendEmailConfirmation";

    private sealed class RecordingEmailSender(bool fail = false) : IEmailSender
    {
        public ConcurrentQueue<(string Email, string Body)> Messages { get; } = new();

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (fail) throw new InvalidOperationException("Synthetic delivery failure");
            Messages.Enqueue((email, htmlMessage));
            return Task.CompletedTask;
        }
    }

    private WebApplicationFactory<Program> WithSender(RecordingEmailSender sender, TimeProvider? clock = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sender);
            if (clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            }
        }));

    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private async Task<(string Id, string Email, string Code)> NewAccountAsync()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"verification.{Guid.NewGuid():N}@fixtures.test";
        var user = new ApplicationUser
        {
            UserName = email, Email = email, EmailConfirmed = false,
            FirstName = "Email", LastName = "Verification", FullName = "Email Verification",
        };
        var created = await users.CreateAsync(user, PortalFactory.Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        return (user.Id, email, WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)));
    }

    private static async Task<HttpResponseMessage> ResendAsync(HttpClient client, string email, string query = "")
    {
        var path = ResendPath + query;
        var html = await client.GetStringAsync(path);
        return await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["__RequestVerificationToken"] = StaffAccounts.Token(html),
        }));
    }

    private WebApplicationFactory<Program> ThrottledReplica() => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services => services.Configure<PortalRateLimitOptions>(options => options.EmailPerAccount = 1)));

    private static async Task<HttpResponseMessage> RepeatedRecipientAsync(HttpClient client, string path, string field, string recipient)
    {
        var page = await client.GetStringAsync(path.Split('?')[0]);
        return await client.PostAsync(path, new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new(field, recipient), new(field, $"nonce.{Guid.NewGuid():N}@fixtures.test"),
            new("__RequestVerificationToken", StaffAccounts.Token(page))
        }));
    }

    private async Task ConfirmAccountAsync(string userId)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(userId))!;
        user.EmailConfirmed = true;
        Assert.True((await users.UpdateAsync(user)).Succeeded);
    }

    private async Task AssertQueuedRecipientAsync(WebApplicationFactory<Program> host, string userId, string recipient)
    {
        var message = await QueuedEmailAsync(userId);
        var protector = host.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("DRMC.AccountEmail.v1");
        var payload = System.Text.Json.JsonSerializer.Deserialize<AccountEmailPayload>(protector.Unprotect(message.ProtectedPayload!));
        Assert.Equal(recipient, payload!.Recipient);
    }

    [Theory]
    [InlineData("/Identity/Account/ForgotPassword", true)]
    [InlineData("/Identity/Account/ForgotPassword", false)]
    [InlineData(ResendPath, true)]
    [InlineData(ResendPath, false)]
    public async Task Repeated_email_fields_cannot_bypass_the_shared_recipient_limit_across_hosts(string path, bool existing)
    {
        var account = await NewAccountAsync();
        if (path.EndsWith("ForgotPassword", StringComparison.Ordinal)) await ConfirmAccountAsync(account.Id);
        var recipient = existing ? account.Email : $"unknown.{Guid.NewGuid():N}@fixtures.test";
        using var first = ThrottledReplica();
        using var second = ThrottledReplica();
        using var firstClient = Client(first);
        using var secondClient = Client(second);
        var accepted = await RepeatedRecipientAsync(firstClient, path, "Input.Email", recipient);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        var limited = await RepeatedRecipientAsync(secondClient, path, "Input.Email", recipient);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        if (existing) await AssertQueuedRecipientAsync(first, account.Id, recipient);
        else
        {
            using var scope = first.Services.CreateScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AccountEmails.AnyAsync(m => m.UserId == account.Id));
        }
    }

    [Theory]
    [InlineData("ChangeEmail")]
    [InlineData("SendVerificationEmail")]
    public async Task Manage_email_limits_the_bound_destination_or_stored_verification_address_across_hosts(string handler)
    {
        var account = await NewAccountAsync();
        await ConfirmAccountAsync(account.Id);
        var destination = $"destination.{Guid.NewGuid():N}@fixtures.test";
        using var first = ThrottledReplica();
        using var second = ThrottledReplica();
        using var firstClient = Client(first);
        using var secondClient = Client(second);
        await firstClient.SignInSuccessfullyAsync(account.Email, PortalFactory.Password);
        await secondClient.SignInSuccessfullyAsync(account.Email, PortalFactory.Password);
        var path = "/Identity/Account/Manage/Email?handler=" + handler;
        var accepted = await RepeatedRecipientAsync(firstClient, path, "Input.NewEmail", destination);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        var secondDestination = handler == "ChangeEmail" ? destination : $"ignored.{Guid.NewGuid():N}@fixtures.test";
        var limited = await RepeatedRecipientAsync(secondClient, path, "Input.NewEmail", secondDestination);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        await AssertQueuedRecipientAsync(first, account.Id, handler == "ChangeEmail" ? destination : account.Email);
    }

    [Theory]
    [InlineData("en", "Check your email", "Send a new confirmation link")]
    [InlineData("fil", "Tingnan ang iyong email", "Magpadala ng bagong confirmation link")]
    [InlineData("ceb", "Tan-awa ang imong email", "Pagpadala og bag-ong confirmation link")]
    public async Task Registration_confirmation_shows_address_next_steps_and_localized_resend(string culture, string title, string action)
    {
        var client = factory.CreatePortalClient();
        var response = await client.GetAsync($"/Identity/Account/RegisterConfirmation?email=patient%40fixtures.test&returnUrl=%2FPatient%2FRadiology&culture={culture}&ui-culture={culture}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(title, html);
        Assert.Contains(action, html);
        Assert.Contains("patient@fixtures.test", html);
        Assert.Contains("auth-verification-steps", html);
        Assert.Contains(ResendPath + "?returnUrl=%2FPatient%2FRadiology", html);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("is-success", html);
    }

    [Theory]
    [InlineData("/Identity/Account/RegisterConfirmation")]
    [InlineData("/Identity/Account/RegisterConfirmation?email=invalid")]
    public async Task Registration_confirmation_without_valid_email_returns_to_registration(string path)
    {
        var response = await factory.CreatePortalClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Register?", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("en", "Check your email", "If this email address belongs to a confirmed account", "Request another reset link")]
    [InlineData("fil", "Tingnan ang iyong email", "Kung ang email address na ito ay para sa isang kumpirmadong account", "Humiling ng panibagong reset link")]
    [InlineData("ceb", "Tan-awa ang imong email", "Kung kini nga email address iya sa usa ka nakumpirmang account", "Pangayo og laing reset link")]
    public async Task Password_reset_confirmation_uses_localized_portal_card_and_generic_next_steps(string culture, string title, string body, string action)
    {
        using var client = factory.CreatePortalClient();
        var response = await client.GetAsync($"/Identity/Account/ForgotPasswordConfirmation?culture={culture}&ui-culture={culture}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(title, html);
        Assert.Contains(body, html);
        Assert.Contains(action, html);
        Assert.Contains("auth-card auth-verification-card", html);
        Assert.Contains("bi-envelope-check", html);
        Assert.Contains("auth-verification-trust", html);
        Assert.Contains("href=\"/Identity/Account/Login\"", html);
        Assert.Contains("href=\"/Identity/Account/ForgotPassword\"", html);
        Assert.Contains("images/drmc-masthead", html);
        Assert.DoesNotContain("auth-verification-recipient", html);
        Assert.DoesNotContain("is-success", html);
        Assert.DoesNotContain("Forgot password confirmation", html);
    }

    [Fact]
    public async Task Valid_confirmation_updates_account_and_preserves_sign_in_destination_without_signing_in()
    {
        var account = await NewAccountAsync();
        var client = factory.CreatePortalClient();
        var response = await client.GetAsync($"/Identity/Account/ConfirmEmail?userId={account.Id}&code={account.Code}&returnUrl=%2FPatient%2FRadiology");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Your email is confirmed", html);
        Assert.Contains("auth-verification-icon is-success", html);
        Assert.Contains("/Identity/Account/Login?returnUrl=%2FPatient%2FRadiology", html);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Patient/Home")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.FindByIdAsync(account.Id))!.EmailConfirmed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?userId=missing&code=%%%")]
    [InlineData("?userId=missing&code=dG9rZW4")]
    public async Task Invalid_confirmation_renders_recovery_with_bad_request_status(string query)
    {
        var response = await factory.CreatePortalClient().GetAsync("/Identity/Account/ConfirmEmail" + query);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("This link is no longer valid", html);
        Assert.Contains(ResendPath, html);
        Assert.DoesNotContain("auth-verification-icon is-success", html);
    }

    [Fact]
    public async Task Wrong_token_does_not_confirm_an_existing_account()
    {
        var account = await NewAccountAsync();
        var response = await factory.CreatePortalClient().GetAsync($"/Identity/Account/ConfirmEmail?userId={account.Id}&code=d3JvbmctdG9rZW4");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.False((await users.FindByIdAsync(account.Id))!.EmailConfirmed);
    }

    [Fact]
    public async Task Expired_confirmation_token_renders_recovery_without_confirming_account()
    {
        var account = await NewAccountAsync();
        using var expiredHost = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromTicks(-1))));
        var response = await Client(expiredHost).GetAsync($"/Identity/Account/ConfirmEmail?userId={account.Id}&code={account.Code}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("This link is no longer valid", await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.False((await users.FindByIdAsync(account.Id))!.EmailConfirmed);
    }

    [Theory]
    [InlineData("RegisterConfirmation?email=patient%40fixtures.test&")]
    [InlineData("ResendEmailConfirmation?")]
    [InlineData("ConfirmEmail?")]
    public async Task Verification_actions_discard_external_return_urls(string page)
    {
        var response = await factory.CreatePortalClient().GetAsync("/Identity/Account/" + page + "returnUrl=https%3A%2F%2Fexternal.invalid%2F");
        var html = await response.Content.ReadAsStringAsync();
        // The shell's language form retains the current page query, but navigation must stay local.
        var destinations = Regex.Matches(html, "(?:href|action)=\"([^\"]+)\"").Select(match => match.Groups[1].Value);
        Assert.DoesNotContain(destinations, destination => destination.Contains("external.invalid"));
        Assert.Contains("returnUrl=%2FPatient%2FHome", html);
    }

    [Theory]
    [InlineData("en", "Enter your email address.", "Enter a valid email address")]
    [InlineData("fil", "Ilagay ang iyong email address.", "Maglagay ng wastong email address")]
    [InlineData("ceb", "Isulod ang imong email address.", "Isulod ang balidong email address")]
    public async Task Resend_validates_email_with_localized_feedback(string culture, string required, string invalid)
    {
        var client = factory.CreatePortalClient();
        var query = $"?culture={culture}&ui-culture={culture}";
        var empty = await ResendAsync(client, "", query);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Contains(required, WebUtility.HtmlDecode(await empty.Content.ReadAsStringAsync()));
        var malformed = await ResendAsync(client, "invalid", query);
        Assert.Equal(HttpStatusCode.OK, malformed.StatusCode);
        Assert.Contains(invalid, WebUtility.HtmlDecode(await malformed.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Resend_requires_antiforgery_token()
    {
        var response = await factory.CreatePortalClient().PostAsync(ResendPath,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Email"] = "patient@fixtures.test" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Resend_has_generic_feedback_and_sends_only_for_unconfirmed_accounts()
    {
        var account = await NewAccountAsync();
        var sender = new RecordingEmailSender();
        using var host = WithSender(sender);
        var client = Client(host);
        foreach (var email in new[] { account.Email, "missing@fixtures.test", PortalFactory.PrimaryEmail })
        {
            var response = await ResendAsync(client, email, "?returnUrl=%2FPatient%2FRadiology");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var html = WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!));
            Assert.Contains("If this email address belongs to an account that needs confirmation", html);
            Assert.DoesNotContain(email, html);
            Assert.DoesNotContain("is-success", html);
            Assert.Contains("/Identity/Account/Login?returnUrl=%2FPatient%2FRadiology", html);
            // Refreshing the redirected page must not submit another delivery request.
            await client.GetAsync(response.Headers.Location!);
        }

        await host.Services.GetRequiredService<AccountEmailDispatcher>().DispatchOnceAsync();
        var delivered = Assert.Single(sender.Messages, message => message.Email == account.Email);
        Assert.Equal(account.Email, delivered.Email);
        var href = ActionUrl(delivered.Body);
        Assert.Contains("returnUrl=%2FPatient%2FRadiology", href);
        var confirmation = await client.GetAsync(href);
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        Assert.Contains("Your email is confirmed", await confirmation.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Resend_delivery_failure_does_not_disclose_account_status()
    {
        var account = await NewAccountAsync();
        using var host = WithSender(new RecordingEmailSender(fail: true));
        var client = Client(host);
        var response = await ResendAsync(client, account.Email);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!));
        Assert.Contains("If this email address belongs to an account that needs confirmation", html);
        Assert.DoesNotContain("Synthetic delivery failure", html);
    }

    private async Task<AccountEmail> QueuedEmailAsync(string userId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AccountEmails.AsNoTracking()
            .SingleAsync(m => m.UserId == userId);
    }

    private async Task<AccountEmailPayload> PayloadAsync(string userId)
    {
        var message = await QueuedEmailAsync(userId);
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("DRMC.AccountEmail.v1");
        return System.Text.Json.JsonSerializer.Deserialize<AccountEmailPayload>(protector.Unprotect(message.ProtectedPayload!))!;
    }

    private static string ActionUrl(string html) => WebUtility.HtmlDecode(
        Regex.Match(html, "href=[\"']([^\"']+)[\"']").Groups[1].Value);

    private static Uri AssertBrandedPayload(AccountEmailPayload payload, string culture, string action, string destination)
    {
        Assert.Contains("DRMC Patient Portal", payload.Body);
        Assert.Contains($"cid:{PortalEmailRenderer.SealContentId}", payload.Body);
        Assert.Contains($"lang=\"{culture}\"", payload.Body);
        Assert.Contains(action, WebUtility.HtmlDecode(payload.Body));
        Assert.NotNull(payload.PlainTextBody);
        Assert.Contains(action, payload.PlainTextBody);
        Assert.DoesNotContain("<table", payload.PlainTextBody);
        var url = new Uri(ActionUrl(payload.Body));
        Assert.Equal(destination, url.AbsolutePath);
        Assert.Equal("https", url.Scheme);
        Assert.Equal("localhost", url.Host);
        Assert.Contains(url.AbsoluteUri, payload.PlainTextBody);
        if (url.Query.Contains('&')) Assert.Contains("&amp;", payload.Body);
        Assert.DoesNotContain("&amp;", payload.PlainTextBody);
        return url;
    }

    [Theory]
    [InlineData("en", "Confirm email address")]
    [InlineData("fil", "Kumpirmahin ang email address")]
    [InlineData("ceb", "Kumpirmaha ang email address")]
    public async Task Registration_renders_localized_html_and_plain_text_inside_the_signup_transaction(string culture, string action)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<PatientRegistrationOptions>(options => options.RequireHospitalRecordCode = false)));
        using var client = Client(host);
        var email = $"signup-email.{Guid.NewGuid():N}@fixtures.test";
        var path = $"/Identity/Account/Register?culture={culture}&ui-culture={culture}&returnUrl=%2FPatient%2FRadiology";
        var response = await client.PostFormAsync(path, path, SignupForms.Signup(email));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        Assert.NotNull(user);
        Assert.False(user.EmailConfirmed);
        var payload = await PayloadAsync(user.Id);
        Assert.Equal(email, payload.Recipient);
        var url = AssertBrandedPayload(payload, culture, action, "/Identity/Account/ConfirmEmail");
        Assert.Equal("/Patient/Radiology", QueryHelpers.ParseQuery(url.Query)["returnUrl"]);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Entry(user).ReloadAsync();
        Assert.True(user.EmailConfirmed);
    }

    [Theory]
    [InlineData("en", "Confirm email address")]
    [InlineData("fil", "Kumpirmahin ang email address")]
    [InlineData("ceb", "Kumpirmaha ang email address")]
    public async Task Resend_renders_request_culture_before_enqueue_and_preserves_confirmation_destination(string culture, string action)
    {
        var account = await NewAccountAsync();
        using var client = factory.CreatePortalClient();
        var response = await ResendAsync(client, account.Email,
            $"?culture={culture}&ui-culture={culture}&returnUrl=%2FPatient%2FRadiology");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var payload = await PayloadAsync(account.Id);
        var url = AssertBrandedPayload(payload, culture, action, "/Identity/Account/ConfirmEmail");
        var query = QueryHelpers.ParseQuery(url.Query);
        Assert.Equal(account.Id, query["userId"]);
        Assert.Equal("/Patient/Radiology", query["returnUrl"]);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("en", "Reset your password")]
    [InlineData("fil", "Palitan ang iyong password")]
    [InlineData("ceb", "Ilisi ang imong password")]
    public async Task Password_reset_renders_both_localized_views_and_a_valid_reset_token(string culture, string action)
    {
        var account = await NewAccountAsync();
        await ConfirmAccountAsync(account.Id);
        using var client = factory.CreatePortalClient();
        var path = $"/Identity/Account/ForgotPassword?culture={culture}&ui-culture={culture}";
        var response = await client.PostFormAsync(path, path, new Dictionary<string, string> { ["Input.Email"] = account.Email });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var payload = await PayloadAsync(account.Id);
        var url = AssertBrandedPayload(payload, culture, action, "/Identity/Account/ResetPassword");
        var code = QueryHelpers.ParseQuery(url.Query)["code"].ToString();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(account.Id))!;
        Assert.True(await users.VerifyUserTokenAsync(user, users.Options.Tokens.PasswordResetTokenProvider,
            UserManager<ApplicationUser>.ResetPasswordTokenPurpose, Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code))));
    }

    [Theory]
    [InlineData("en", "ChangeEmail", "Confirm new email address")]
    [InlineData("fil", "ChangeEmail", "Kumpirmahin ang bagong email address")]
    [InlineData("ceb", "ChangeEmail", "Kumpirmaha ang bag-ong email address")]
    [InlineData("en", "SendVerificationEmail", "Confirm email address")]
    [InlineData("fil", "SendVerificationEmail", "Kumpirmahin ang email address")]
    [InlineData("ceb", "SendVerificationEmail", "Kumpirmaha ang email address")]
    public async Task Account_settings_render_localized_views_for_the_bound_destination(string culture, string handler, string action)
    {
        var account = await NewAccountAsync();
        await ConfirmAccountAsync(account.Id);
        using var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(account.Email, PortalFactory.Password);
        var destination = $"changed.{Guid.NewGuid():N}@fixtures.test";
        var page = $"/Identity/Account/Manage/Email?culture={culture}&ui-culture={culture}";
        var response = await client.PostFormAsync(page, page + "&handler=" + handler,
            new Dictionary<string, string> { ["Input.NewEmail"] = destination });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var payload = await PayloadAsync(account.Id);
        var changing = handler == "ChangeEmail";
        Assert.Equal(changing ? destination : account.Email, payload.Recipient);
        var url = AssertBrandedPayload(payload, culture, action,
            changing ? "/Identity/Account/ConfirmEmailChange" : "/Identity/Account/ConfirmEmail");
        var query = QueryHelpers.ParseQuery(url.Query);
        Assert.Equal(account.Id, query["userId"]);
        if (changing)
        {
            Assert.Equal(destination, query["email"]);
            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(account.Id))!;
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(query["code"].ToString()));
            Assert.True((await users.ChangeEmailAsync(user, destination, token)).Succeeded);
        }
    }

    private async Task DispatchEmailAsync(WebApplicationFactory<Program> host, string userId, int expectedAttempts)
    {
        for (var pass = 0; pass < 20; pass++)
        {
            await host.Services.GetRequiredService<AccountEmailDispatcher>().DispatchOnceAsync();
            if ((await QueuedEmailAsync(userId)).Attempts >= expectedAttempts) return;
        }
        Assert.Fail("The queued verification email was not dispatched within the bounded batch count.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verification_email_survives_host_restart_and_recovers_an_abandoned_lease(bool abandonedLease)
    {
        var account = await NewAccountAsync();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        Guid messageId;
        using (var original = WithSender(new RecordingEmailSender(), clock))
        {
            var response = await ResendAsync(Client(original), account.Email);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var queued = await QueuedEmailAsync(account.Id);
            messageId = queued.Id;
            Assert.Equal(AccountEmailStatus.Pending, queued.Status);
            Assert.NotNull(queued.ProtectedPayload);
            if (abandonedLease)
            {
                using var scope = original.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AccountEmails
                    .Where(m => m.Id == messageId).ExecuteUpdateAsync(set => set
                        .SetProperty(m => m.Status, AccountEmailStatus.Sending)
                        .SetProperty(m => m.Attempts, 1)
                        .SetProperty(m => m.LeaseOwner, "crashed-verification-host")
                        .SetProperty(m => m.LeaseExpiresUtc, (DateTimeOffset?)clock.Now.AddMinutes(2)));
            }
        }

        var sender = new RecordingEmailSender();
        using var restarted = WithSender(sender, clock);
        if (abandonedLease)
        {
            await restarted.Services.GetRequiredService<AccountEmailDispatcher>().DispatchOnceAsync();
            Assert.DoesNotContain(sender.Messages, m => m.Email == account.Email);
            clock.Now = clock.Now.AddMinutes(3);
        }
        await DispatchEmailAsync(restarted, account.Id, abandonedLease ? 2 : 1);
        var delivered = await QueuedEmailAsync(account.Id);
        Assert.Equal(messageId, delivered.Id);
        Assert.Equal(AccountEmailStatus.Delivered, delivered.Status);
        Assert.Equal(abandonedLease ? 2 : 1, delivered.Attempts);
        Assert.Null(delivered.ProtectedPayload);
        Assert.Null(delivered.LeaseOwner);
        Assert.Single(sender.Messages, m => m.Email == account.Email);
    }

    [Fact]
    public async Task Transient_verification_failure_retains_the_message_and_retries_after_host_restart()
    {
        var account = await NewAccountAsync();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        AccountEmail failed;
        using (var original = WithSender(new RecordingEmailSender(fail: true), clock))
        {
            Assert.Equal(HttpStatusCode.Redirect, (await ResendAsync(Client(original), account.Email)).StatusCode);
            await DispatchEmailAsync(original, account.Id, 1);
            failed = await QueuedEmailAsync(account.Id);
            Assert.Equal(AccountEmailStatus.Pending, failed.Status);
            Assert.NotNull(failed.ProtectedPayload);
            Assert.True(failed.NextAttemptUtc > clock.Now);
        }

        var sender = new RecordingEmailSender();
        using var restarted = WithSender(sender, clock);
        await restarted.Services.GetRequiredService<AccountEmailDispatcher>().DispatchOnceAsync();
        Assert.DoesNotContain(sender.Messages, m => m.Email == account.Email);
        clock.Now = failed.NextAttemptUtc.AddSeconds(1);
        await DispatchEmailAsync(restarted, account.Id, 2);
        var delivered = await QueuedEmailAsync(account.Id);
        Assert.Equal(failed.Id, delivered.Id);
        Assert.Equal(AccountEmailStatus.Delivered, delivered.Status);
        Assert.Equal(2, delivered.Attempts);
        Assert.Null(delivered.ProtectedPayload);
        Assert.Single(sender.Messages, m => m.Email == account.Email);
    }

    [Theory]
    [InlineData("ResetPassword")]
    [InlineData("ConfirmEmailChange")]
    public async Task Other_invalid_identity_links_keep_existing_validation(string page)
    {
        var response = await factory.CreatePortalClient().GetAsync("/Identity/Account/" + page);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unconfirmed_sign_in_preserves_destination_on_the_resend_action()
    {
        var account = await NewAccountAsync();
        var client = factory.CreatePortalClient();
        const string path = "/Identity/Account/Login?returnUrl=%2FPatient%2FRadiology";
        var token = StaffAccounts.Token(await client.GetStringAsync(path));
        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = account.Email,
            ["Input.Password"] = PortalFactory.Password,
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(ResendPath + "?returnUrl=%2FPatient%2FRadiology", await response.Content.ReadAsStringAsync());
    }
}
