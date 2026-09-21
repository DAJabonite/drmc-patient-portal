using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace DrmcPatientPortal.BrowserTests;

[Collection("Portal")]
public sealed class ClientRequirementsTests(PortalFixture app)
{
    [Fact]
    public async Task Public_navigation_hides_patient_records_on_desktop_and_mobile()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();

        foreach (var device in new[] { "1440", "small" })
        {
            await using var context = await browser.NewContextAsync(app.Context(device));
            var page = await context.NewPageAsync();
            await page.GotoAsync("/");

            foreach (var path in new[] { "/Patient/Encounters", "/Patient/LabResults", "/Patient/Medications" })
            {
                await Expect(page.Locator($"nav[aria-label='Main navigation'] a[href='{path}'], .mobile-tab-bar a[href='{path}']")).ToHaveCountAsync(0);
            }

            await Expect(page.Locator(".mobile-tab-inner-public")).ToHaveCountAsync(1);
            await Expect(page.Locator(".mobile-tab-inner-public .mobile-tab-item")).ToHaveCountAsync(2);
        }
    }

    [Fact]
    public async Task Dswd_requirement_cards_keep_adjacent_card_collapsed_on_desktop()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("1440"));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/Malasakit/DSWDServices");

        var cards = page.Locator("details.requirement-disclosure");
        await Expect(cards).ToHaveCountAsync(4);
        await cards.Nth(0).Locator("summary").ClickAsync();

        await Expect(cards.Nth(0)).ToHaveAttributeAsync("open", "");
        await Expect(cards.Nth(1)).Not.ToHaveAttributeAsync("open", "");

        var expandedHeight = (await cards.Nth(0).BoundingBoxAsync())!.Height;
        var adjacentHeight = (await cards.Nth(1).BoundingBoxAsync())!.Height;
        Assert.True(expandedHeight > adjacentHeight + 100, $"Expected independent card heights but measured {expandedHeight}px and {adjacentHeight}px.");
    }

    [Fact]
    public async Task Home_login_cta_uses_the_same_warning_color_as_portal_entry()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("small"));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");

        var heroClass = await page.Locator(".hero-actions a[href='/Identity/Account/Login']").GetAttributeAsync("class");
        var portalClass = await page.Locator(".portal-entry-actions a[href='/Identity/Account/Login']").GetAttributeAsync("class");

        Assert.Contains("btn-warning", heroClass);
        Assert.Contains("btn-warning", portalClass);
        Assert.Contains("text-dark", heroClass);
        Assert.Contains("text-dark", portalClass);
    }

    [Fact]
    public async Task Encounters_does_not_offer_a_duplicate_medical_history_shortcut()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("small"));
        var page = await context.NewPageAsync();
        await PortalFixture.SignIn(page);
        await page.GotoAsync("/Patient/Encounters");

        await Expect(page.Locator("main a[href='/Patient/MedicalHistory']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Patient_navigation_uses_visits_and_hides_duplicate_medical_history_shortcuts()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("small"));
        var page = await context.NewPageAsync();
        await PortalFixture.SignIn(page);
        await page.GotoAsync("/Patient/Encounters");

        await Expect(page.Locator("h1")).ToHaveTextAsync("Visits & Care Notes");
        await Expect(page.Locator(".mobile-tab-bar a[href='/Patient/Encounters'] span")).ToHaveTextAsync("Visits");

        await page.Locator(".mobile-menu-trigger").ClickAsync();
        await Expect(page.Locator("#mobileUtilityMenu a[href='/Patient/MedicalHistory']")).ToHaveCountAsync(0);

        await page.GotoAsync("/Patient/Home");
        await Expect(page.Locator("main a[href='/Patient/MedicalHistory']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Audit_history_uses_cards_on_mobile_and_table_on_desktop()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();

        await using (var mobileContext = await browser.NewContextAsync(app.Context("small")))
        {
            var page = await mobileContext.NewPageAsync();
            await PortalFixture.SignIn(page);
            await page.GotoAsync("/Patient/Audit");

            await Expect(page.Locator(".audit-mobile-list")).ToBeVisibleAsync();
            await Expect(page.Locator(".audit-event-card").First).ToBeVisibleAsync();
            await Expect(page.Locator(".table-responsive")).ToBeHiddenAsync();
            await Expect(page.Locator(".audit-technical-details").First).Not.ToHaveAttributeAsync("open", "");
        }

        await using (var desktopContext = await browser.NewContextAsync(app.Context("1440")))
        {
            var page = await desktopContext.NewPageAsync();
            await PortalFixture.SignIn(page);
            await page.GotoAsync("/Patient/Audit");

            await Expect(page.Locator(".audit-mobile-list")).ToBeHiddenAsync();
            await Expect(page.Locator(".table-responsive")).ToBeVisibleAsync();
        }
    }

    [Theory]
    [InlineData("chromium", "1440")]
    [InlineData("webkit", "iphone")]
    [InlineData("chromium", "small")]
    public async Task HistoryAndSubsidyFlow(string engine, string device)
    {
        await using var browser = await app.Engine(engine).LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context(device));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/Patient/MedicalHistory");
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Identity/Account/Login"));
        await PortalFixture.SignIn(page);
        await page.GotoAsync("/Patient/MedicalHistory");
        await Expect(page.Locator("h1")).ToHaveTextAsync("Medical history");
        Assert.Equal(5, await page.Locator(".mobile-tab-item").CountAsync());
        Assert.Equal(0, await page.Locator(".mobile-tab-bar a[href*='MedicalHistory'], nav[aria-label='Main navigation'] > .container > ul a[href*='MedicalHistory']").CountAsync());
        await page.Locator("main a[href*='category=ER']").ClickAsync();
        await Expect(page.Locator("#heading-ER")).ToBeVisibleAsync();
        await Expect(page.Locator("#heading-OPD")).ToHaveCountAsync(0);
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        await page.ScreenshotAsync(new() { Path = Path.Combine(app.Artifacts, $"client-history-{device}.png"), FullPage = true });

        await page.GotoAsync("/Malasakit/Apply");
        // Each device can revisit the same saved current application.
        if (page.Url.EndsWith("/Apply"))
        {
            await page.Locator("#Need").SelectOptionAsync("Medicines");
            await page.Locator("#ReportedPayment").SelectOptionAsync("Paid");
            await page.Locator("#GovernmentIdReady").CheckAsync();
            await page.Locator("#AcknowledgedReview").CheckAsync();
            Assert.Empty(await ResponsiveTests.LayoutProblems(page));
            await page.ScreenshotAsync(new() { Path = Path.Combine(app.Artifacts, $"client-apply-{device}.png"), FullPage = true });
            await page.GetByRole(AriaRole.Button, new() { Name = "Save subsidy application" }).ClickAsync();
        }
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Malasakit/Status$"));
        await Expect(page.Locator("main")).ToContainTextAsync("Pending assessment");
        await Expect(page.Locator("main")).ToContainTextAsync("Not yet determined");
        await Expect(page.Locator("main")).ToContainTextAsync("Not confirmed");
        await page.Locator("#CostDocumentReady").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save document checklist" }).ClickAsync();
        await Expect(page.Locator("main [role='status']")).ToContainTextAsync("has been saved");
        await page.ReloadAsync();
        await Expect(page.Locator("#CostDocumentReady")).ToBeCheckedAsync();
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        await page.ScreenshotAsync(new() { Path = Path.Combine(app.Artifacts, $"client-subsidy-{device}.png"), FullPage = true });
        await Expect(page.Locator("main a[href='https://drmc.doh.gov.ph/citizens-charter/']")).ToBeVisibleAsync();
        await Expect(page.Locator("main a[href='https://drmc.doh.gov.ph/anti-red-tape-act/']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task OtherGovernmentId_RequiresDescription_AndSupportsManualRegistration()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("small"));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/Identity/Account/Register");
        await SignupTests.AcceptNotice(page);
        await page.Locator("#selectIdType").SelectOptionAsync("Other government-issued ID");
        await page.Locator("#btnManualSkip").ClickAsync();
        await Expect(page.Locator("#wizardStep1")).ToBeVisibleAsync();
        await Expect(page.Locator("#stepFeedback1")).ToContainTextAsync("government issuer");
        await page.Locator("#Input_OtherGovernmentIdName").FillAsync("Voter's ID - COMELEC");
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        await page.ScreenshotAsync(new() { Path = Path.Combine(app.Artifacts, "client-other-government-id.png"), FullPage = true });
        await page.Locator("#btnManualSkip").ClickAsync();
        await page.Locator("#txtFirstName").FillAsync("Test");
        await page.Locator("#txtLastName").FillAsync("GovernmentId");
        await page.Locator("#txtIdNumber").FillAsync("TEST-" + Guid.NewGuid().ToString("N")[..8]);
        await page.Locator("#btnGoToStep4").ClickAsync();
        await page.Locator("input[name='Input.Email']").FillAsync($"gov-id-{Guid.NewGuid():N}@example.test");
        await page.Locator("input[name='Input.Mobile']").FillAsync("9175550123");
        await page.Locator("#regPassword").FillAsync("Browser!2026Test");
        await page.Locator("#regConfirmPassword").FillAsync("Browser!2026Test");
        await page.Locator("#registerSubmit").ClickAsync();
        await page.WaitForURLAsync("**/Patient/Home");
        await page.GotoAsync("/Malasakit/Status");
        await Expect(page.Locator("main")).ToContainTextAsync("No subsidy application yet");
        await page.GotoAsync("/Patient/MedicalHistory");
        await Expect(page.Locator("main")).ToContainTextAsync("No records in this category");
    }
}
