using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace DrmcPatientPortal.BrowserTests;

[Collection("Portal")]
public sealed class OpdGuideTests(PortalFixture app)
{
    private static readonly string[] FacilityKeys = ["main", "bucas", "ccm", "acc"];

    [Fact]
    public async Task FacilityAccordionShowsEachFlowDirectlyBelowItsControlAndPassesAccessibilityChecks()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("1440"));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/OpdGuide");

        var triggers = page.Locator(".opd-facility-trigger");
        Assert.Equal(4, await triggers.CountAsync());
        Assert.Collection(
            await triggers.AllTextContentsAsync(),
            text => Assert.Contains("Main OPD", text),
            text => Assert.Contains("BUCAS OPD", text),
            text => Assert.Contains("Cancer Center for Mindanao OPD (CCM OPD)", text),
            text => Assert.Contains("Ambulatory Care Center OPD (ACC OPD)", text));

        Assert.Equal(0, await page.GetByRole(AriaRole.Heading, new() { Name = "Patient Consultation Sequence" }).CountAsync());
        await Expect(page.GetByRole(AriaRole.Note)).ToContainTextAsync("start the entire sequence over");
        await Expect(page.Locator("main a[href='https://drmc.doh.gov.ph/citizens-charter/']")).ToBeVisibleAsync();
        await Expect(page.Locator("main a[href='https://drmc.doh.gov.ph/anti-red-tape-act/']")).ToBeVisibleAsync();

        await AssertAllPanelsHidden(page);
        await SelectFacility(page, "main");
        await AssertOnlyPanelVisible(page, "main");
        Assert.Equal(1, await page.Locator("#opd-item-main > h3 + #opd-panel-main").CountAsync());
        await Expect(page.Locator("#opd-panel-main .opd-step-pre")).ToContainTextAsync("PACD");
        Assert.Equal(7, await page.Locator("#opd-panel-main .opd-step-item").CountAsync());
        await Expect(page.Locator("#opd-panel-main .opd-step-item").Nth(1)).ToContainTextAsync("initial screening questions");
        await Expect(page.Locator("#opd-panel-main .opd-step-item").Nth(5)).ToContainTextAsync("pay ₱150");
        await Expect(page.Locator("#opd-panel-main .opd-step-item").Nth(5)).ToContainTextAsync("pay ₱0");

        await SelectFacility(page, "bucas");
        Assert.Equal(9, await page.Locator("#opd-panel-bucas .opd-step-item").CountAsync());
        Assert.Equal(4, await page.Locator("#opd-panel-bucas .opd-step-destinations li").CountAsync());
        await Expect(page.Locator("#opd-panel-bucas .opd-step-item").Nth(8)).ToContainTextAsync("Conditional");

        await SelectFacility(page, "ccm");
        Assert.Equal(5, await page.Locator("#opd-panel-ccm .opd-step-item").CountAsync());
        await Expect(page.Locator("#opd-panel-ccm .opd-step-item").First).ToContainTextAsync("queue number");
        await Expect(page.Locator("#opd-panel-ccm .opd-verification-note")).ToContainTextAsync("pending confirmation from DRMC");

        await SelectFacility(page, "acc");
        Assert.Equal(5, await page.Locator("#opd-panel-acc .opd-step-item").CountAsync());
        Assert.Equal(4, await page.Locator("#opd-panel-acc .opd-step-destinations li").CountAsync());
        await Expect(page.Locator("#opd-panel-acc")).ToContainTextAsync("Day Surgery");

        await page.Locator("#opd-trigger-main").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Expect(page.Locator("#opd-trigger-bucas")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(1440, 900, "desktop")]
    [InlineData(390, 844, "mobile")]
    [InlineData(355, 628, "mobile-narrow")]
    public async Task FacilityPanelsCaptureWithoutOverflow(int width, int height, string device)
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.Url,
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/OpdGuide");
        // Full-page screenshots are stitched from scrolled viewports. Fixed off-canvas
        // helpers can otherwise be painted between tiles even though users never see them.
        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = ".skip-link { display: none !important; }"
        });

        foreach (var key in FacilityKeys)
        {
            await SelectFacility(page, key);
            Assert.Empty(await ResponsiveTests.LayoutProblems(page));
            Assert.Empty(await OpdLayoutProblems(page));

            var result = await page.RunAxe(new AxeRunOptions
            {
                RunOnly = new RunOnlyOptions
                {
                    Type = "tag",
                    Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"]
                }
            });
            Assert.True(
                !result.Violations.Any(),
                string.Join(Environment.NewLine, result.Violations.Select(violation =>
                    $"{key}: {violation.Id} — {string.Join(", ", violation.Nodes.Select(node => node.Target))}")));
            // Axe can leave the global skip link focused while probing contrast.
            // Restore focus to the selected facility control before taking the artifact.
            await page.Locator($"#opd-trigger-{key}").FocusAsync();

            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(app.Artifacts, $"opd-guide-{device}-{key}.png"),
                FullPage = true
            });
        }
    }

    [Fact]
    public async Task FacilityPanelsStayClosedUntilAUserClicksATrigger()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("small"));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/OpdGuide?facility=bucas");

        await AssertAllPanelsHidden(page);
        await page.Locator("#opd-trigger-bucas").ClickAsync();
        await AssertOnlyPanelVisible(page, "bucas");
    }

    private static async Task SelectFacility(IPage page, string key)
    {
        var trigger = page.Locator($"#opd-trigger-{key}");
        if (await trigger.GetAttributeAsync("aria-expanded") != "true")
            await trigger.ClickAsync();
        await Expect(page.Locator($"#opd-panel-{key}")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("show"));
        await AssertOnlyPanelVisible(page, key);
    }

    private static async Task AssertOnlyPanelVisible(IPage page, string selectedKey)
    {
        foreach (var key in FacilityKeys)
        {
            if (key == selectedKey)
                await Expect(page.Locator($"#opd-panel-{key}")).ToBeVisibleAsync();
            else
                await Expect(page.Locator($"#opd-panel-{key}")).ToBeHiddenAsync();
        }
    }

    private static async Task AssertAllPanelsHidden(IPage page)
    {
        foreach (var key in FacilityKeys)
        {
            await Expect(page.Locator($"#opd-trigger-{key}")).ToHaveAttributeAsync("aria-expanded", "false");
            await Expect(page.Locator($"#opd-panel-{key}")).ToBeHiddenAsync();
        }
    }

    private static Task<string[]> OpdLayoutProblems(IPage page) => page.EvaluateAsync<string[]>("""
        () => {
          const failures = [];
          const viewportWidth = document.documentElement.clientWidth;
          const selector = [
            '.opd-facility-trigger', '.opd-facility-name', '.opd-stepper-container',
            '.opd-step-item', '.opd-step-content', '.opd-step-title', '.opd-step-detail',
            '.opd-step-location', '.opd-step-requirement', '.opd-step-branches',
            '.opd-step-destinations', '.opd-step-destinations li', '.opd-verification-note'
          ].join(',');

          for (const element of document.querySelectorAll(selector)) {
            if (!element.getClientRects().length) continue;
            const rect = element.getBoundingClientRect();
            if (rect.left < -1 || rect.right > viewportWidth + 1)
              failures.push(`Outside viewport: ${element.className}`);
            if (element.scrollWidth > element.clientWidth + 1)
              failures.push(`Content overflow: ${element.className}`);

            const borderedContainer = element.closest('.opd-step-requirement, .opd-step-destinations li');
            if (borderedContainer && borderedContainer !== element) {
              const containerRect = borderedContainer.getBoundingClientRect();
              if (rect.left < containerRect.left - 1 || rect.right > containerRect.right + 1)
                failures.push(`Child outside border: ${element.className}`);
            }
          }
          return [...new Set(failures)];
        }
        """);
}
