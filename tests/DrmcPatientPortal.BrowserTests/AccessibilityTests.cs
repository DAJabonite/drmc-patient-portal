using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

namespace DrmcPatientPortal.BrowserTests;

[Collection("Portal")]
public sealed class AccessibilityTests(PortalFixture app)
{
    [Theory]
    [InlineData("1440")]
    [InlineData("small")]
    public async Task Screens_meet_automated_WCAG_AA_checks(string device)
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context(device));
        var page = await context.NewPageAsync();
        var failures = new List<string>();
        var review = new List<string>();
        foreach (var (routes, signedIn) in new[] { (ResponsiveTests.PublicScreens, false), (ResponsiveTests.PatientScreens, true) })
        {
            if (signedIn) await PortalFixture.SignIn(page);
            foreach (var route in routes)
            {
                await page.GotoAsync(route);
                var result = await page.RunAxe(new AxeRunOptions
                {
                    RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] }
                });
                foreach (var violation in result.Violations)
                    failures.AddRange(violation.Nodes.Select(node => $"{route} [{violation.Id}] {node.Html} {Newtonsoft.Json.JsonConvert.SerializeObject(node.Any)}"));
                foreach (var incomplete in result.Incomplete)
                    review.AddRange(incomplete.Nodes.Select(node => $"{route} [{incomplete.Id}] {node.Html}"));
            }
        }
        await File.WriteAllLinesAsync(Path.Combine(app.Artifacts, $"accessibility-{device}.txt"), failures);
        await File.WriteAllLinesAsync(Path.Combine(app.Artifacts, $"accessibility-review-{device}.txt"), review);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
