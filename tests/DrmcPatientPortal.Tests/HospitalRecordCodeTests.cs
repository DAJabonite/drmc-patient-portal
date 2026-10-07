using System.Net;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Areas.Identity.Pages.Account;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Tests;

public sealed class HospitalRecordCodeFormatTests
{
    [Fact]
    public void Generated_codes_are_15_unambiguous_characters_in_three_groups()
    {
        var codes = Enumerable.Range(0, 500).Select(_ => HospitalRecordCodes.Generate()).ToArray();
        Assert.All(codes, code => Assert.Matches("^[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$", code));
        Assert.Equal(codes.Length, codes.Distinct().Count());
    }

    [Theory]
    [InlineData("7kq2m-x9d4t-h3vnp", "7KQ2MX9D4TH3VNP")]
    [InlineData(" 7KQ2M X9D4T H3VNP ", "7KQ2MX9D4TH3VNP")]
    [InlineData("7KQ2MX9D4TH3VNP", "7KQ2MX9D4TH3VNP")]
    [InlineData("OIL2M-X9D4T-H3VNP", "0112MX9D4TH3VNP")]
    public void Normalize_accepts_case_spacing_and_lookalike_variants(string input, string expected) =>
        Assert.Equal(expected, HospitalRecordCodes.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7KQ2M-X9D4T")]
    [InlineData("7KQ2M-X9D4T-H3VNP-1")]
    [InlineData("7KQ2M-X9D4T-H3VN!")]
    [InlineData("7KQ2M-X9D4T-H3VNU")]
    public void Normalize_rejects_malformed_codes(string? input) => Assert.Null(HospitalRecordCodes.Normalize(input));

    [Fact]
    public void Signup_url_keeps_the_code_in_the_fragment()
    {
        var url = HospitalRecordCodes.SignupUrl("https://portal.test/Identity/Account/Register", "7KQ2M-X9D4T-H3VNP");
        var uri = new Uri(url);
        Assert.Equal("/Identity/Account/Register", uri.AbsolutePath);
        Assert.Equal("", uri.Query);
        Assert.Equal("#code=7KQ2M-X9D4T-H3VNP", uri.Fragment);
    }
}

[Collection(PortalCollection.Name)]
public sealed partial class HospitalRecordCodeTests(PortalFactory factory)
{
    private static readonly DateTime BirthDate = new(1988, 3, 14);

    [GeneratedRegex("data-registration-code[^>]*>([0-9A-Z]{5}-[0-9A-Z]{5}-[0-9A-Z]{5})<")]
    private static partial Regex IssuedCode();

    private async Task<int> NewPatientAsync(DateTime? birthDate = null, bool noBirthDate = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var record = new PatientRecord { FullName = "Synthetic Code Patient " + Guid.NewGuid().ToString("N")[..6], DateOfBirth = noBirthDate ? null : birthDate ?? BirthDate };
        db.PatientRecords.Add(record);
        await db.SaveChangesAsync();
        return record.Id;
    }

    private static Dictionary<string, string> IssueForm(bool verified = true) => new()
    {
        ["Input.IssuingPoint"] = "Red Star Clinic",
        ["Input.IdentityVerified"] = verified ? "true" : "false",
    };

    private async Task<(HttpResponseMessage Response, string Html)> IssueAsync(HttpClient staff, int patientId, bool verified = true)
    {
        var response = await staff.PostFormAsync($"/Admin/RegistrationCodes/Create/{patientId}", $"/Admin/RegistrationCodes/Create/{patientId}", IssueForm(verified));
        return (response, await response.Content.ReadAsStringAsync());
    }

    private async Task<string> IssueCodeAsync(int patientId)
    {
        var (response, html) = await IssueAsync(await StaffAccounts.PatientServicesAsync(factory), patientId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var match = IssuedCode().Match(html);
        Assert.True(match.Success, "Issued page did not show the code.");
        return match.Groups[1].Value;
    }

    private static Dictionary<string, string> Signup(string email, string? code, DateTime? birthDate) => new()
    {
        ["Input.FirstName"] = "Synthetic",
        ["Input.LastName"] = "Signup",
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
        ["Input.HospitalRecordCode"] = code ?? "",
    };

    private static async Task<HttpResponseMessage> SignupAsync(HttpClient client, string email, string? code, DateTime? birthDate) =>
        await client.PostFormAsync("/Identity/Account/Register", "/Identity/Account/Register", Signup(email, code, birthDate));

    private static string Email() => $"code.{Guid.NewGuid():N}@fixtures.test";

    private async Task<(ApplicationUser? User, PatientRecord Record, PatientRegistrationCode[] Codes)> StateAsync(int patientId, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email);
        var record = await db.PatientRecords.AsNoTracking().SingleAsync(p => p.Id == patientId);
        var codes = await db.PatientRegistrationCodes.AsNoTracking().Where(c => c.PatientRecordId == patientId).OrderBy(c => c.Id).ToArrayAsync();
        return (user, record, codes);
    }

    [Fact]
    public async Task Staff_issue_a_one_time_slip_with_qr_and_only_a_hash_is_stored()
    {
        var patientId = await NewPatientAsync();
        var staff = await StaffAccounts.PatientServicesAsync(factory);
        var form = await staff.GetStringAsync($"/Admin/RegistrationCodes/Create/{patientId}");
        Assert.Contains("March 14, 1988", form);
        Assert.Contains("Red Star Clinic", form);

        var (response, html) = await IssueAsync(staff, patientId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("<svg", html);
        var code = IssuedCode().Match(html).Groups[1].Value;
        var canonical = HospitalRecordCodes.Normalize(code)!;

        var (_, _, codes) = await StateAsync(patientId, "none");
        var stored = Assert.Single(codes);
        Assert.Equal(HospitalRecordCodes.Hash(canonical), stored.CodeHash);
        Assert.Equal(canonical[^5..], stored.CodeHint);
        Assert.Equal("Red Star Clinic", stored.IssuingPoint);
        Assert.Equal(StaffAccounts.PatientServicesEmail, stored.IssuedByEmail);
        Assert.InRange(stored.ExpiresAtUtc - stored.IssuedAtUtc, TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1), TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1));

        var list = await staff.GetStringAsync("/Admin/RegistrationCodes");
        Assert.Contains("-" + stored.CodeHint, list);
        Assert.DoesNotContain(code, list);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.AdminAuditLogs.AnyAsync(a => a.Entity == "RegistrationCodes" && a.Action == "Issue" && a.SubjectPatientId == patientId));
    }

    [Fact]
    public async Task Issuing_requires_identity_check_birth_date_and_an_unlinked_record()
    {
        var staff = await StaffAccounts.PatientServicesAsync(factory);
        var patientId = await NewPatientAsync();
        var (unverified, html) = await IssueAsync(staff, patientId, verified: false);
        Assert.Equal(HttpStatusCode.OK, unverified.StatusCode);
        Assert.Contains("Confirm that staff checked the patient&#x27;s identity.", html);

        var noBirthDate = await NewPatientAsync(noBirthDate: true);
        Assert.Contains("Add the patient&#x27;s birth date", (await IssueAsync(staff, noBirthDate)).Html);

        var linked = await NewPatientAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var owner = await db.Users.SingleAsync(u => u.Email == StaffAccounts.PatientServicesEmail);
            if (!await db.PatientRecords.AnyAsync(p => p.PortalUserId == owner.Id))
                await db.PatientRecords.Where(p => p.Id == linked).ExecuteUpdateAsync(s => s.SetProperty(p => p.PortalUserId, owner.Id));
        }
        Assert.Contains("already linked to a portal account", (await IssueAsync(staff, linked)).Html);

        Assert.Empty((await StateAsync(patientId, "none")).Codes);
        Assert.Empty((await StateAsync(noBirthDate, "none")).Codes);
        Assert.Empty((await StateAsync(linked, "none")).Codes);
    }

    [Fact]
    public async Task Signup_with_a_valid_code_links_the_record_and_uses_the_code()
    {
        var patientId = await NewPatientAsync();
        var code = await IssueCodeAsync(patientId);
        var email = Email();
        var response = await SignupAsync(factory.CreatePortalClient(), email, code.ToLowerInvariant().Replace("-", " "), BirthDate);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("RegisterConfirmation", response.Headers.Location?.OriginalString);

        var (user, record, codes) = await StateAsync(patientId, email);
        Assert.NotNull(user);
        Assert.Equal(user.Id, record.PortalUserId);
        var used = Assert.Single(codes);
        Assert.Equal(RegistrationCodeStatus.Redeemed, used.StatusAt(DateTime.UtcNow));
        Assert.Equal(user.Id, used.RedeemedByUserId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == user.Id && a.Action == "LINK_HOSPITAL_RECORD"));

        // A used code cannot be replayed for a second account.
        var replayEmail = Email();
        var replay = await SignupAsync(factory.CreatePortalClient(), replayEmail, code, BirthDate);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Contains(System.Net.WebUtility.HtmlEncode(RegisterModel.HospitalRecordCodeError), await replay.Content.ReadAsStringAsync());
        Assert.Null((await StateAsync(patientId, replayEmail)).User);
    }

    [Fact]
    public async Task Wrong_birth_date_revoked_expired_or_unknown_codes_create_no_account()
    {
        var patientId = await NewPatientAsync();
        var code = await IssueCodeAsync(patientId);
        var error = System.Net.WebUtility.HtmlEncode(RegisterModel.HospitalRecordCodeError);

        async Task AssertRejectedAsync(string candidate, DateTime? birthDate)
        {
            var email = Email();
            var response = await SignupAsync(factory.CreatePortalClient(), email, candidate, birthDate);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(error, await response.Content.ReadAsStringAsync());
            var state = await StateAsync(patientId, email);
            Assert.Null(state.User);
            Assert.Null(state.Record.PortalUserId);
        }

        await AssertRejectedAsync(code, BirthDate.AddDays(1));
        await AssertRejectedAsync(code, null);
        await AssertRejectedAsync(HospitalRecordCodes.Generate(), BirthDate);
        Assert.Equal(RegistrationCodeStatus.Active, Assert.Single((await StateAsync(patientId, "none")).Codes).StatusAt(DateTime.UtcNow));

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PatientRegistrationCodes.Where(c => c.PatientRecordId == patientId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        await AssertRejectedAsync(code, BirthDate);

        var fresh = await IssueCodeAsync(patientId);
        var staff = await StaffAccounts.PatientServicesAsync(factory);
        var list = await staff.GetStringAsync("/Admin/RegistrationCodes");
        var active = (await StateAsync(patientId, "none")).Codes.Single(c => c.StatusAt(DateTime.UtcNow) == RegistrationCodeStatus.Active);
        var revoke = await staff.PostAsync($"/Admin/RegistrationCodes/Revoke/{active.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["rowVersion"] = Convert.ToBase64String(active.RowVersion),
            ["__RequestVerificationToken"] = StaffAccounts.Token(list),
        }));
        Assert.Equal(HttpStatusCode.Redirect, revoke.StatusCode);
        Assert.Equal(RegistrationCodeStatus.Revoked, (await StateAsync(patientId, "none")).Codes.Single(c => c.Id == active.Id).StatusAt(DateTime.UtcNow));
        await AssertRejectedAsync(fresh, BirthDate);
    }

    [Fact]
    public async Task Issuing_a_new_code_revokes_the_earlier_one()
    {
        var patientId = await NewPatientAsync();
        var first = await IssueCodeAsync(patientId);
        var second = await IssueCodeAsync(patientId);
        Assert.NotEqual(first, second);
        var codes = (await StateAsync(patientId, "none")).Codes;
        Assert.Equal([RegistrationCodeStatus.Revoked, RegistrationCodeStatus.Active], codes.Select(c => c.StatusAt(DateTime.UtcNow)));

        var email = Email();
        Assert.Equal(HttpStatusCode.OK, (await SignupAsync(factory.CreatePortalClient(), email, first, BirthDate)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await SignupAsync(factory.CreatePortalClient(), email, second, BirthDate)).StatusCode);
        var state = await StateAsync(patientId, email);
        Assert.Equal(state.User!.Id, state.Record.PortalUserId);
    }

    [Fact]
    public async Task Concurrent_signups_with_one_code_link_exactly_one_account()
    {
        var patientId = await NewPatientAsync();
        var code = await IssueCodeAsync(patientId);
        var emails = Enumerable.Range(0, 4).Select(_ => Email()).ToArray();
        var clients = emails.Select(_ => factory.CreatePortalClient()).ToArray();
        // Load every form first so the posts race on redemption, not on page rendering.
        var pages = await Task.WhenAll(clients.Select(c => c.GetStringAsync("/Identity/Account/Register")));
        var responses = await Task.WhenAll(clients.Select((c, i) => c.PostAsync("/Identity/Account/Register", new FormUrlEncodedContent(
            new Dictionary<string, string>(Signup(emails[i], code, BirthDate)) { ["__RequestVerificationToken"] = StaffAccounts.Token(pages[i]) }))));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Redirect);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var created = await db.Users.Where(u => emails.Contains(u.Email!)).ToListAsync();
        var winner = Assert.Single(created);
        Assert.Equal(winner.Id, (await db.PatientRecords.SingleAsync(p => p.Id == patientId)).PortalUserId);
    }

    [Fact]
    public async Task Malformed_code_shows_a_format_error()
    {
        var response = await SignupAsync(factory.CreatePortalClient(), Email(), "ABC-123", BirthDate);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Enter the 15-character code exactly as printed", await response.Content.ReadAsStringAsync());
    }

    private HttpClient OptionalModeClient(out IDisposable host)
    {
        var optional = factory.WithWebHostBuilder(b => b.UseSetting("PatientRegistration:RequireHospitalRecordCode", "false"));
        host = optional;
        return optional.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    }

    [Fact]
    public void Hospital_record_code_is_required_by_default()
    {
        Assert.True(new PatientRegistrationOptions().RequireHospitalRecordCode);
        Assert.True(factory.Services.GetRequiredService<IOptions<PatientRegistrationOptions>>().Value.RequireHospitalRecordCode);
    }

    [Fact]
    public async Task Signup_without_a_code_is_rejected_and_creates_no_account()
    {
        var client = factory.CreatePortalClient();
        var page = await client.GetStringAsync("/Identity/Account/Register");
        Assert.Contains("id=\"hospitalCodeRequirement\"", page);
        Assert.DoesNotMatch("Hospital record code\\s*<span class=\"registration-optional\">", page);
        Assert.Matches("id=\"hospitalRecordCode\"[^>]*data-val-required=", page);
        foreach (var code in new string?[] { null, "", "   " })
        {
            var email = Email();
            var response = await SignupAsync(client, email, code, BirthDate);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Enter the hospital record code you received", await response.Content.ReadAsStringAsync());
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.Users.AnyAsync(u => u.Email == email));
        }
    }

    [Fact]
    public async Task Credentials_step_shows_the_code_field_without_inline_script()
    {
        var html = await factory.CreatePortalClient().GetStringAsync("/Identity/Account/Register");
        Assert.Contains("id=\"hospitalRecordCode\"", html);
        Assert.Contains("No code yet? Visit the PACD", html);
        Assert.Contains("Red Star Clinic", html);
        Assert.DoesNotMatch("<script(?![^>]*src=)[^>]*>", html);
    }

    [Fact]
    public async Task Optional_mode_keeps_open_signup_and_leaves_the_account_unlinked()
    {
        var client = OptionalModeClient(out var host);
        using (host)
        {
            var page = await client.GetStringAsync("/Identity/Account/Register");
            Assert.Matches("Hospital record code\\s*<span class=\"registration-optional\">\\(Optional\\)", page);
            Assert.DoesNotContain("id=\"hospitalCodeRequirement\"", page);
            var email = Email();
            var response = await SignupAsync(client, email, null, null);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == email);
            Assert.False(await db.PatientRecords.AnyAsync(p => p.PortalUserId == user.Id));
        }
    }
}
