using System.Text.RegularExpressions;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace DrmcPatientPortal.BrowserTests;

[Collection("Portal")]
public sealed class WorkflowTests(PortalFixture app)
{
    [Theory]
    [InlineData("chromium", "1440", false)]
    [InlineData("webkit", "iphone", true)]
    public async Task Booking_triage_checkin_and_cancellation(string engine, string device, bool emergency)
    {
        await using var browser = await app.Engine(engine).LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context(device));
        var page = await context.NewPageAsync();
        await PortalFixture.SignIn(page);
        await page.GotoAsync("/Appointments/Book");
        await page.Locator("#bookingForm button[type=submit]").ClickAsync();
        await Expect(page.Locator("[data-valmsg-for=ChiefComplaint]")).Not.ToBeEmptyAsync();
        await page.Locator("#ChiefComplaint").FillAsync("Browser regression test follow-up consultation");
        await page.Locator("#AppointmentDate").FillAsync(DateTime.Today.AddDays(3).ToString("yyyy-MM-dd"));
        await page.Locator("#bookingForm button[type=submit]").ClickAsync();
        await page.WaitForURLAsync("**/Appointments/Confirmation?*");
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        var confirmation = page.Url;
        var cancelAction = await page.Locator("form[action*='/Cancel']").GetAttributeAsync("action");
        var id = Regex.Match(cancelAction!, @"(?:/|id=)(\d+)(?:$|&)").Groups[1].Value;
        Assert.NotEmpty(id);
        await page.Locator("a[href*='/Appointments/CheckIn']").ClickAsync();
        await Expect(page.Locator("main")).ToContainTextAsync("DRMC-");
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        await page.GotoAsync("/Patient/Triage/Start/" + id);
        await page.Locator("#ChiefComplaint").FillAsync("Test symptoms for triage regression");
        await page.Locator("#SymptomDurationDays").FillAsync("2");
        if (emergency) await page.Locator("#redChest").CheckAsync();
        await page.Locator("main button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(emergency ? "**/Patient/Triage/EmergencyWarning*" : "**/Patient/Triage/Summary/*");
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        await page.ScreenshotAsync(new() { Path = Path.Combine(app.Artifacts, $"triage-{device}.png"), FullPage = true });
        await page.GotoAsync(confirmation);
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await page.RunAndWaitForResponseAsync(() => page.Locator("form[action*='/Cancel'] button").ClickAsync(),
            response => response.Url.Contains("/Appointments/Cancel") && response.Request.Method == "POST");
        await page.GotoAsync(confirmation);
        await Expect(page.Locator("main")).ToContainTextAsync("Cancelled");
        await Expect(page.Locator("form[action*='/Cancel']")).ToHaveCountAsync(0);
        await Expect(page.Locator(".page-header")).Not.ToContainTextAsync("Booking Confirmed");
        await page.Locator("a[href*='/Appointments/CheckIn']").ClickAsync();
        await Expect(page.Locator("main")).ToContainTextAsync("Cancelled");
        await Expect(page.Locator("main")).Not.ToContainTextAsync("Valid Confirmed Appointment");
    }

    [Theory]
    [InlineData("chromium", "1440")]
    [InlineData("webkit", "iphone")]
    public async Task Registration_steps_validation_and_success(string engine, string device)
    {
        await using var browser = await app.Engine(engine).LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context(device));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/Identity/Account/Register");
        await SignupTests.AcceptNotice(page);
        await Expect(page.Locator("#btnGoToCapture")).ToBeDisabledAsync();
        await page.Locator("#selectIdType").SelectOptionAsync("Philippine Passport");
        await page.Locator("#btnGoToCapture").ClickAsync();
        await Expect(page.Locator("#wizardStep2")).ToBeVisibleAsync();
        await Expect(page.Locator("#btnTriggerOcr")).ToBeDisabledAsync();
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        await page.Locator("#btnManualSkipFromStep2").ClickAsync();
        await Expect(page.Locator("#wizardStep3")).ToBeVisibleAsync();
        await Expect(page.Locator("#wizardStep3 h2")).ToBeFocusedAsync();
        await page.Locator("#txtFirstName").FillAsync("Browser");
        await page.Locator("#txtLastName").FillAsync("Testpatient");
        await page.Locator("#txtIdNumber").FillAsync("TEST" + Guid.NewGuid().ToString("N")[..8]);
        await page.Locator("#btnGoToStep4").ClickAsync();
        await Expect(page.Locator("#wizardStep4")).ToBeVisibleAsync();
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        var email = $"browser-{Guid.NewGuid():N}@example.test";
        await page.Locator("input[name='Input.Email']").FillAsync(email);
        await page.Locator("input[name='Input.Mobile']").FillAsync("9175550123");
        await page.Locator("#regPassword").FillAsync("Browser!2026Test");
        await page.Locator("#regConfirmPassword").FillAsync("mismatch");
        await page.Locator("#registerSubmit").ClickAsync();
        await Expect(page.Locator("[data-valmsg-for='Input.ConfirmPassword']")).Not.ToBeEmptyAsync();
        await page.Locator("#regConfirmPassword").FillAsync("Browser!2026Test");
        await page.Locator("#registerSubmit").ClickAsync();
        await page.WaitForURLAsync("**/Patient/Home");
        await Expect(page.Locator("main")).ToContainTextAsync("No upcoming appointment");
        await page.GotoAsync("/Identity/Account/Manage/EnableAuthenticator");
        var key = await page.Locator("kbd").InnerTextAsync();
        await page.Locator("input[name='Input.Code']").FillAsync(Totp(key));
        await page.Locator("#send-code button[type=submit]").ClickAsync();
        await page.WaitForURLAsync("**/Manage/TwoFactorAuthentication");
        await context.ClearCookiesAsync();
        await page.GotoAsync("/Identity/Account/Login");
        await page.Locator("input[name='Input.Email']").FillAsync(email);
        await page.Locator("input[name='Input.Password']").FillAsync("Browser!2026Test");
        await page.Locator("#login-submit").ClickAsync();
        await page.WaitForURLAsync("**/Account/LoginWith2fa?*");
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        await page.Locator("input[name='Input.TwoFactorCode']").FillAsync(Totp(key));
        await page.Locator("main button[type=submit]").ClickAsync();
        await page.WaitForURLAsync("**/Patient/Home");
    }

    private static string Totp(string key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(key.ToUpperInvariant().Where(alphabet.Contains)
            .Select(c => Convert.ToString(alphabet.IndexOf(c), 2).PadLeft(5, '0')));
        var bytes = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        var counter = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(bytes, counter);
        var offset = hash[^1] & 15;
        var number = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & int.MaxValue;
        return (number % 1_000_000).ToString("D6");
    }

    [Fact]
    public async Task Empty_account_redirects_search_language_and_profile()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("small"));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/Patient/Encounters");
        Assert.Contains("ReturnUrl", page.Url);
        await page.Locator("input[name='Input.Email']").FillAsync("juan@drmc.doh.gov.ph");
        await page.Locator("input[name='Input.Password']").FillAsync("J@uan2026");
        await page.Locator("#login-submit").ClickAsync();
        await page.WaitForURLAsync("**/Patient/Encounters");
        await Expect(page.Locator("main .card")).ToContainTextAsync("No");
        foreach (var route in new[] { "/Patient/Home", "/Patient/LabResults", "/Patient/Medications" })
        {
            await page.GotoAsync(route);
            Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        }
        await page.GotoAsync("/Patient/LabResults");
        await page.Locator("input[name=search]").FillAsync("no-matching-test");
        await page.Locator(".record-search button").ClickAsync();
        await Expect(page.Locator("main")).ToContainTextAsync("No laboratory results found");
        await page.Locator(".mobile-menu-trigger").TapAsync();
        await page.Locator("#mobileUtilityMenu details summary").ClickAsync();
        await page.Locator("#mobileUtilityMenu button").Filter(new() { HasText = "Filipino" }).ClickAsync();
        await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "fil");
        Assert.Contains("search=no-matching-test", page.Url);
        await page.GotoAsync("/Identity/Account/Manage");
        await page.Locator("input[name='Input.FullName']").FillAsync("Juan Browser Test");
        await page.Locator("#update-profile-button").ClickAsync();
        await Expect(page.Locator(".alert-success")).ToBeVisibleAsync();
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
    }

    [Fact]
    public async Task Assistance_refill_and_queue_states()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("small"));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/Malasakit/Navigator");
        await page.Locator("#needMeds").CheckAsync();
        await page.Locator("main button[type=submit]").ClickAsync();
        await page.WaitForURLAsync("**/Malasakit/Assess");
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        await PortalFixture.SignIn(page);
        await page.GotoAsync("/Patient/Medications/Details/1");
        await page.Locator("form[action*='RequestRefill'] button").ClickAsync();
        await page.WaitForURLAsync("**/Patient/Medications/RefillStatus/*");
        await Expect(page.Locator("main")).ToContainTextAsync("Requested");
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        await page.GotoAsync("/Queue");
        await page.Locator("#refreshQueueBtn").ClickAsync();
        await Expect(page.Locator("#queueRefreshStatus")).Not.ToBeEmptyAsync();
        await page.RouteAsync("**/Queue/Live", route => route.FulfillAsync(new() { Status = 503, Body = "Unavailable" }));
        await page.Locator("#refreshQueueBtn").ClickAsync();
        await Expect(page.Locator("#queueRefreshStatus")).ToContainTextAsync("failed");
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
    }

    [Fact]
    public async Task Keyboard_reflow_reduced_motion_and_print()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        // A 1280px desktop zoomed to 200% has a 640 CSS-pixel layout viewport.
        await using var context = await browser.NewContextAsync(new() { BaseURL = app.Url,
            ViewportSize = new() { Width = 640, Height = 450 }, DeviceScaleFactor = 2,
            ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await page.Keyboard.PressAsync("Tab");
        await Expect(page.Locator(".skip-link")).ToBeFocusedAsync();
        Assert.True(await page.Locator(".skip-link").EvaluateAsync<bool>("el => getComputedStyle(el).outlineStyle !== 'none'"));
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("main")).ToBeFocusedAsync();
        await PortalFixture.SignIn(page);
        foreach (var route in new[] { "/Patient/Home", "/Patient/Encounters", "/Appointments/Book", "/Identity/Account/Manage" })
        {
            await page.GotoAsync(route);
            Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        }
        await page.GotoAsync("/Patient/Encounters/Print/1");
        await page.EmulateMediaAsync(new() { Media = Media.Print });
        await Expect(page.Locator(".mobile-tab-bar")).ToBeHiddenAsync();
        await Expect(page.Locator(".mobile-header")).ToBeHiddenAsync();
        await Expect(page.Locator(".print-container")).ToContainTextAsync("DRMC-ENC-2026-0412");
    }
}
