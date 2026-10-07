using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Areas.Identity.Pages.Account;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DrmcPatientPortal.Tests;

internal static class SignupForms
{
    public static Dictionary<string, string> Signup(string email, string? recordCode = null, string? invitation = null, DateTime? birthDate = null) => new()
    {
        ["Input.FirstName"] = "Synthetic",
        ["Input.LastName"] = "Staff",
        ["Input.Address"] = "Purok 1, Bajada, Davao City, Davao del Sur",
        ["Input.DateOfBirth"] = birthDate?.ToString("yyyy-MM-dd") ?? "",
        ["Input.Email"] = email,
        ["Input.Mobile"] = "9171234567",
        ["Input.IdType"] = PhilippineIdTypes.DriversLicense,
        ["Input.IdNumber"] = "N01-23-456789",
        ["Input.Password"] = PortalFactory.Password,
        ["Input.ConfirmPassword"] = PortalFactory.Password,
        ["Input.PrivacyConsent"] = "true",
        ["Input.IsManualEntry"] = "true",
        ["Input.OcrConfidence"] = "0",
        ["Input.HospitalRecordCode"] = recordCode ?? "",
        ["Input.StaffInvitationCode"] = invitation ?? "",
    };

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Dictionary<string, string> form) =>
        client.PostFormAsync("/Identity/Account/Register", "/Identity/Account/Register", form);
}

[Collection(PortalCollection.Name)]
public sealed partial class StaffInvitationTests(PortalFactory factory)
{
    [GeneratedRegex("data-registration-code[^>]*>([0-9A-Z]{5}-[0-9A-Z]{5}-[0-9A-Z]{5})<")]
    private static partial Regex IssuedCode();

    private sealed class RecordingEmailSender : IEmailSender
    {
        public ConcurrentQueue<(string Email, string Subject, string Body)> Messages { get; } = new();
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            Messages.Enqueue((email, subject, htmlMessage));
            return Task.CompletedTask;
        }
    }

    private static string Email() => $"staff.{Guid.NewGuid():N}@fixtures.test";

    // Inserts an invitation directly so signup rules can be tested without the Admin UI.
    private async Task<(int Id, string Code)> SeedAsync(string email, string role = "LabStaff", TimeSpan? expiresIn = null, bool revoked = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var normalizer = scope.ServiceProvider.GetRequiredService<ILookupNormalizer>();
        var adminId = await StaffAccounts.EnsureAccountAsync(factory.Services, StaffAccounts.AdminEmail, true, "Admin");
        var formatted = HospitalRecordCodes.Generate();
        var canonical = HospitalRecordCodes.Normalize(formatted)!;
        var now = DateTime.UtcNow;
        var invitation = new StaffInvitation
        {
            Email = email, NormalizedEmail = normalizer.NormalizeEmail(email), Role = role,
            CodeHash = HospitalRecordCodes.Hash(canonical), CodeHint = HospitalRecordCodes.Hint(canonical),
            InvitedById = adminId, InvitedByEmail = StaffAccounts.AdminEmail, CreatedAtUtc = now.AddMinutes(-5),
            ExpiresAtUtc = now + (expiresIn ?? TimeSpan.FromDays(3)), RevokedAtUtc = revoked ? now : null,
        };
        db.StaffInvitations.Add(invitation);
        await db.SaveChangesAsync();
        return (invitation.Id, formatted);
    }

    private async Task<(ApplicationUser? User, StaffInvitation Invitation, IList<string> Roles)> StateAsync(int invitationId, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        var invitation = await db.StaffInvitations.AsNoTracking().SingleAsync(i => i.Id == invitationId);
        return (user, invitation, user is null ? [] : await users.GetRolesAsync(user));
    }

    // Completes account setup the way a person would: 2FA on, then the emailed confirmation link.
    private async Task ConfirmWithTwoFactorAsync(string email)
    {
        string link;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            await users.ResetAuthenticatorKeyAsync(user);
            await users.SetTwoFactorEnabledAsync(user, true);
            var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(user)));
            link = $"/Identity/Account/ConfirmEmail?userId={user.Id}&code={token}";
        }
        var response = await factory.CreatePortalClient().GetAsync(link);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_invites_staff_with_a_one_time_link_emailed_and_only_a_hash_stored()
    {
        var sender = new RecordingEmailSender();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(sender); }));
        var admin = await StaffAccounts.SignInAsync(host, StaffAccounts.AdminEmail, "Admin");
        var email = Email();
        var response = await admin.PostFormAsync("/Admin/StaffInvitations/Create", "/Admin/StaffInvitations/Create",
            new Dictionary<string, string> { ["Email"] = email, ["Role"] = "RadiologyStaff" });
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var code = IssuedCode().Match(html).Groups[1].Value;
        Assert.Matches("^[0-9A-Z]{5}-[0-9A-Z]{5}-[0-9A-Z]{5}$", code);
        Assert.Contains("<svg", html);
        Assert.Contains("We emailed the signup link", html);
        var message = Assert.Single(sender.Messages, m => m.Email == email);
        Assert.Contains("/Identity/Account/Register#invite=" + code, message.Body);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.StaffInvitations.AsNoTracking().SingleAsync(i => i.Email == email);
        Assert.Equal(HospitalRecordCodes.Hash(HospitalRecordCodes.Normalize(code)!), stored.CodeHash);
        Assert.Equal(code[^5..], stored.CodeHint);
        Assert.Equal("RadiologyStaff", stored.Role);
        Assert.True(await db.AdminAuditLogs.AnyAsync(a => a.Entity == "StaffInvitations" && a.Action == "Invite" && a.RecordKey == stored.Id.ToString()));
        Assert.DoesNotContain(code, await (await admin.GetAsync("/Admin/StaffInvitations")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invitations_never_grant_admin_and_are_not_sent_to_existing_accounts()
    {
        var admin = await StaffAccounts.AdminAsync(factory);
        var adminInvite = Email();
        var adminRole = await admin.PostFormAsync("/Admin/StaffInvitations/Create", "/Admin/StaffInvitations/Create", new Dictionary<string, string> { ["Email"] = adminInvite, ["Role"] = "Admin" });
        Assert.Contains("Select the staff role to grant.", await adminRole.Content.ReadAsStringAsync());
        var existing = await admin.PostFormAsync("/Admin/StaffInvitations/Create", "/Admin/StaffInvitations/Create",
            new Dictionary<string, string> { ["Email"] = PortalFactory.PrimaryEmail.ToUpperInvariant(), ["Role"] = "LabStaff" });
        Assert.Contains("An account with this email already exists", await existing.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.StaffInvitations.AnyAsync(i => i.Email == adminInvite || i.NormalizedEmail == PortalFactory.PrimaryEmail.ToUpperInvariant()));
    }

    [Fact]
    public async Task Staff_member_signs_up_with_an_invitation_and_gets_the_role_after_email_and_2fa()
    {
        var email = Email();
        var (id, code) = await SeedAsync(email, "PatientServicesStaff");
        var page = await factory.CreatePortalClient().GetStringAsync("/Identity/Account/Register");
        Assert.Contains("id=\"staffInvitationPanel\"", page);
        Assert.Contains("id=\"btnUseStaffInvite\"", page);

        var response = await SignupForms.PostAsync(factory.CreatePortalClient(), SignupForms.Signup(email.ToUpperInvariant(), invitation: code.ToLowerInvariant()));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var (user, invitation, roles) = await StateAsync(id, email);
        Assert.NotNull(user);
        Assert.Equal(user!.Id, invitation.AcceptedByUserId);
        Assert.Null(invitation.RoleGrantedAtUtc);
        Assert.Empty(roles);

        await ConfirmWithTwoFactorAsync(email);
        (user, invitation, roles) = await StateAsync(id, email);
        Assert.NotNull(invitation.RoleGrantedAtUtc);
        Assert.Equal(["PatientServicesStaff"], roles);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.AdminAuditLogs.AnyAsync(a => a.Entity == "StaffAccess" && a.Action == "Grant" && a.RecordKey == user!.Id && a.ActorEmail == StaffAccounts.AdminEmail));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == user!.Id && a.Action == "ACCEPT_STAFF_INVITATION"));

        // Activation runs once: calling it again grants nothing more.
        var activator = scope.ServiceProvider.GetRequiredService<StaffInvitationActivator>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await activator.TryActivateAsync((await users.FindByEmailAsync(email))!));
    }

    [Fact]
    public async Task Wrong_email_unknown_revoked_expired_or_used_invitations_create_no_account()
    {
        var email = Email();
        var (_, code) = await SeedAsync(email);
        var revokedEmail = Email();
        var (_, revoked) = await SeedAsync(revokedEmail, revoked: true);
        var expiredEmail = Email();
        var (_, expired) = await SeedAsync(expiredEmail, expiresIn: TimeSpan.FromMinutes(-1));

        async Task AssertRejectedAsync(string signupEmail, string invitation)
        {
            var response = await SignupForms.PostAsync(factory.CreatePortalClient(), SignupForms.Signup(signupEmail, invitation: invitation));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains(System.Net.WebUtility.HtmlEncode(RegisterModel.StaffInvitationError), html);
            using var scope = factory.Services.CreateScope();
            Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(signupEmail));
        }

        await AssertRejectedAsync(Email(), code);                       // someone else's invitation
        await AssertRejectedAsync(email, HospitalRecordCodes.Generate()); // unknown code
        await AssertRejectedAsync(expiredEmail, expired);
        var malformed = await SignupForms.PostAsync(factory.CreatePortalClient(), SignupForms.Signup(Email(), invitation: "ABC-123"));
        Assert.Contains("Enter the 15-character invitation code", await malformed.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Redirect, (await SignupForms.PostAsync(factory.CreatePortalClient(), SignupForms.Signup(email, invitation: code))).StatusCode);
        var replay = Email();
        await AssertRejectedAsync(replay, code);
        await AssertRejectedAsync(revokedEmail, revoked);
    }

    [Fact]
    public async Task Revoking_after_signup_means_no_role_is_granted()
    {
        var email = Email();
        var (id, code) = await SeedAsync(email);
        Assert.Equal(HttpStatusCode.Redirect, (await SignupForms.PostAsync(factory.CreatePortalClient(), SignupForms.Signup(email, invitation: code))).StatusCode);
        var admin = await StaffAccounts.AdminAsync(factory);
        var list = await (await admin.GetAsync("/Admin/StaffInvitations?search=" + Uri.EscapeDataString(email))).Content.ReadAsStringAsync();
        Assert.Contains("Awaiting setup", list);
        var rowVersion = Regex.Match(list, "name=\"rowVersion\" value=\"([^\"]+)\"").Groups[1].Value;
        var revoke = await admin.PostFormAsync("/Admin/StaffInvitations?search=" + Uri.EscapeDataString(email), $"/Admin/StaffInvitations/Revoke/{id}",
            new Dictionary<string, string> { ["rowVersion"] = System.Net.WebUtility.HtmlDecode(rowVersion) });
        Assert.Equal(HttpStatusCode.Redirect, revoke.StatusCode);

        await ConfirmWithTwoFactorAsync(email);
        var (_, invitation, roles) = await StateAsync(id, email);
        Assert.NotNull(invitation.RevokedAtUtc);
        Assert.Null(invitation.RoleGrantedAtUtc);
        Assert.Empty(roles);
    }

    [Fact]
    public async Task An_admin_role_in_an_invitation_row_is_never_granted()
    {
        var email = Email();
        var (id, code) = await SeedAsync(email, role: "Admin");
        Assert.Equal(HttpStatusCode.Redirect, (await SignupForms.PostAsync(factory.CreatePortalClient(), SignupForms.Signup(email, invitation: code))).StatusCode);
        await ConfirmWithTwoFactorAsync(email);
        var (_, invitation, roles) = await StateAsync(id, email);
        Assert.Null(invitation.RoleGrantedAtUtc);
        Assert.Empty(roles);
    }

    [Fact]
    public async Task Without_an_invitation_or_code_signup_is_still_rejected_and_bootstrap_is_closed()
    {
        // The shared test host has AdminBootstrap disabled, so no email is exempt.
        var email = Email();
        var response = await SignupForms.PostAsync(factory.CreatePortalClient(), SignupForms.Signup(email));
        Assert.Contains("Enter the hospital record code you received", await response.Content.ReadAsStringAsync());
        var page = await factory.CreatePortalClient().GetStringAsync("/Identity/Account/Register");
        Assert.Matches("id=\"hospitalRecordCode\"[^>]*data-val-required=", page);
    }
}

// First-Admin setup needs a database with no Admin yet, so it gets its own throwaway host.
public sealed class FirstAdminSignupTests : IAsyncLifetime
{
    private const string BootstrapEmail = "first.admin@fixtures.test";
    private readonly PortalFactory _factory = new();
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("AdminBootstrap:Enabled", "true");
            b.UseSetting("AdminBootstrap:Email", BootstrapEmail);
        });
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await ((IAsyncLifetime)_factory).DisposeAsync();
    }

    private HttpClient Client() => _host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    [Fact]
    public async Task Only_the_bootstrap_email_skips_the_code_and_only_until_an_admin_exists()
    {
        // While setup is open the browser does not require the code; the server decides per email.
        var page = await Client().GetStringAsync("/Identity/Account/Register");
        Assert.DoesNotMatch("id=\"hospitalRecordCode\"[^>]*data-val-required=", page);

        var other = await SignupForms.PostAsync(Client(), SignupForms.Signup("someone.else@fixtures.test"));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Contains("Enter the hospital record code you received", await other.Content.ReadAsStringAsync());

        var first = await SignupForms.PostAsync(Client(), SignupForms.Signup(BootstrapEmail.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        using (var scope = _host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(BootstrapEmail))!;
            Assert.Empty(await users.GetRolesAsync(user)); // signup alone never grants Admin
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == user.Id && a.Action == "FIRST_ADMIN_SIGNUP"));
            // Start over without that account, so only the "an Admin exists" rule is tested below.
            await users.DeleteAsync(user);
        }

        // Once any Admin exists, first-Admin setup is closed even for the bootstrap email.
        await StaffAccounts.EnsureAccountAsync(_host.Services, "real.admin@fixtures.test", true, "Admin");
        var closed = await SignupForms.PostAsync(Client(), SignupForms.Signup(BootstrapEmail));
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Contains("Enter the hospital record code you received", await closed.Content.ReadAsStringAsync());
        Assert.Matches("id=\"hospitalRecordCode\"[^>]*data-val-required=", await Client().GetStringAsync("/Identity/Account/Register"));
    }
}
