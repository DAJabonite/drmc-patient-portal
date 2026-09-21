using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace DrmcPatientPortal.BrowserTests;

[Collection("Portal")]
public sealed class HandoffTests(PortalFixture app)
{
    [Theory]
    [InlineData("chromium", "1440")]
    [InlineData("webkit", "iphone")]
    public async Task Visit_links_filters_and_legacy_bookmarks_work(string engine, string device)
    {
        await using var browser = await app.Engine(engine).LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context(device));
        var page = await context.NewPageAsync();
        await PortalFixture.SignIn(page);
        await Expect(page.Locator("main")).Not.ToContainTextAsync("OpdConsultation");
        await page.GetByRole(AriaRole.Link, new() { Name = "View all visits", Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(app.Url + "/Patient/Visits");
        await page.Locator("#encounterDepartmentFilter").SelectOptionAsync("OPD");
        if (device == "1440") await page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("department=OPD"));
        await Expect(page.Locator("main")).ToContainTextAsync("Internal Medicine");
        var response = await page.GotoAsync("/Patient/Encounters/Details/1");
        Assert.Equal(200, response?.Status);
        await Expect(page.Locator("main a[href='/Patient/Visits']").First).ToBeVisibleAsync();
        await page.GotoAsync("/Patient/Visits?department=Emergency");
        await Expect(page.Locator("main")).ToContainTextAsync("No visits match the selected filters.");
        await page.GotoAsync("/Patient/LabResults?dateRange=custom&startDate=2000-01-01&endDate=" + DateTime.Today.ToString("yyyy-MM-dd"));
        await Expect(page.Locator("#customLabDateRange")).ToBeVisibleAsync();
        await Expect(page.Locator("main")).ToContainTextAsync("Complete Blood Count");
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        await page.GotoAsync("/Patient/LabResults?dateRange=custom&startDate=2026-02-28&endDate=2026-02-01");
        await Expect(page.Locator("main [role='alert']")).ToContainTextAsync("start date must be on or before the end date");
        await page.GotoAsync("/Patient/Triage/Summary/1");
        await Expect(page.Locator("main")).ToContainTextAsync("Intake saved locally");
        await Expect(page.Locator("main")).Not.ToContainTextAsync("Synced");
        await Expect(page.Locator("main")).Not.ToContainTextAsync("Verified");
    }
}
