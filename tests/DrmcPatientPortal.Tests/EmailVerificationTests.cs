using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
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

    private WebApplicationFactory<Program> WithSender(RecordingEmailSender sender) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sender);
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

        var delivered = Assert.Single(sender.Messages);
        Assert.Equal(account.Email, delivered.Email);
        var href = WebUtility.HtmlDecode(Regex.Match(delivered.Body, "href='([^']+)'").Groups[1].Value);
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
