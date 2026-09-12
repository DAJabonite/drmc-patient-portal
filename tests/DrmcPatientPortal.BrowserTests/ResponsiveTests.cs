using Microsoft.Playwright;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace DrmcPatientPortal.BrowserTests;

[Collection("Portal")]
public sealed class ResponsiveTests(PortalFixture app, ITestOutputHelper output)
{
    internal static IEnumerable<string> PublicScreens => PublicRoutes;
    internal static IEnumerable<string> PatientScreens => PatientRoutes;
    private static readonly string[] PublicRoutes = ["/", "/Directory", "/Directory/Doctor/1", "/Directory/Department?name=Internal%20Medicine", "/OpdGuide", "/Advisories", "/Advisories/Details?slug=dengue-4s-prevention-alert", "/Queue", "/Queue/Status?ticketNumber=IM-104", "/Queue/Status?ticketNumber=missing", "/Home/Privacy", "/Home/ServiceUnavailable", "/Malasakit", "/Malasakit/Program?code=MALASAKIT", "/Malasakit/Navigator", "/Appointments/Book", "/Appointments/CheckIn?reference=missing", "/Identity/Account/Login", "/Identity/Account/Register", "/Identity/Account/ForgotPassword", "/Identity/Account/AccessDenied", "/Home/Error"];
    private static readonly string[] PatientRoutes = ["/Patient/Home", "/Patient/Encounters", "/Patient/Encounters/Details/1", "/Patient/Encounters/Print/1", "/Patient/LabResults", "/Patient/LabResults/Details/1", "/Patient/LabResults/Print/1", "/Patient/LabResults/Trends", "/Patient/Medications", "/Patient/Medications/Details/1", "/Patient/Medications/RefillStatus/1", "/Patient/Audit", "/Identity/Account/Manage", "/Identity/Account/Manage/TwoFactorAuthentication", "/Identity/Account/Manage/EnableAuthenticator", "/Appointments/Confirmation?reference=DRMC-2026-IM-0192", "/Appointments/CheckIn?reference=DRMC-2026-IM-0192", "/Patient/Triage/Start/2", "/Patient/Triage/Summary/1", "/Patient/Triage/EmergencyWarning"];

    public static TheoryData<string, string, string> Matrix
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var device in new[] { "1440", "1920", "768", "991", "992", "1024", "pixel", "small" })
                data.Add("chromium", device, "en");
            data.Add("webkit", "iphone", "en");
            data.Add("webkit", "landscape", "en");
            data.Add("firefox", "1440", "en");
            data.Add("webkit", "1440", "en");
            foreach (var culture in new[] { "fil", "ceb" })
            {
                data.Add("chromium", "1440", culture);
                data.Add("chromium", "small", culture);
                data.Add("webkit", "iphone", culture);
            }
            return data;
        }
    }

    public static async Task<string[]> LayoutProblems(IPage page) => await page.EvaluateAsync<string[]>("""
        () => {
          const errors = [], width = document.documentElement.clientWidth;
          if (document.documentElement.scrollWidth > width + 1) errors.push(`Document overflow: ${document.documentElement.scrollWidth} > ${width}`);
          for (const el of document.querySelectorAll('main h1, main h2, main h3, main .badge, main .encounter-reference, main .btn, main input:not([type=hidden]), main select, main textarea')) {
            const r = el.getBoundingClientRect(), css = getComputedStyle(el);
            if (!r.width || !r.height || css.visibility === 'hidden' || el.closest('.visually-hidden, .visually-hidden-focusable')) continue;
            // Tables and filter strips deliberately scroll within their own bounds.
            if (el.closest('.table-responsive, .overflow-x-auto')) continue;
            if (r.right > width + 1 || r.left < -1) errors.push(`Outside viewport: ${el.textContent.trim().slice(0,80)}`);
            if (!el.matches('input, select, textarea') && el.scrollWidth > el.clientWidth + 2 && css.overflowX !== 'auto') errors.push(`Clipped text: ${el.textContent.trim().slice(0,80)}`);
            if (el.matches('.btn, h1, h2, h3')) {
              const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
              while (walker.nextNode()) {
                const node = walker.currentNode;
                for (const word of node.textContent.matchAll(/[A-Za-z]{6,}/g)) {
                  const range = document.createRange();
                  range.setStart(node, word.index); range.setEnd(node, word.index + word[0].length);
                  const lines = new Set([...range.getClientRects()].filter(r => r.height > 0).map(r => Math.round(r.top)));
                  if (lines.size > 1) errors.push(`Word split across lines: ${word[0]}`);
                }
              }
            }
          }
          return errors;
        }
        """);

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Every_screen_reflows(string engine, string device, string culture)
    {
        await using var browser = await app.Engine(engine).LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context(device, culture));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        var environment = await page.EvaluateAsync<string>("() => JSON.stringify({viewport:[innerWidth,innerHeight],screen:[screen.width,screen.height],dpr:devicePixelRatio,touch:navigator.maxTouchPoints,coarse:matchMedia('(pointer:coarse)').matches,userAgent:navigator.userAgent})");
        var failures = new List<string>();
        var scriptErrors = new List<string>();
        page.PageError += (_, error) => scriptErrors.Add(error);
        var routes = new List<string>();
        foreach (var group in new[] { PublicRoutes, PatientRoutes })
        {
            if (ReferenceEquals(group, PatientRoutes)) await PortalFixture.SignIn(page);
            foreach (var route in group)
            {
                var response = await page.GotoAsync(route);
                // ServiceUnavailable is an intentional 503 page, and missing check-in is a 404.
                var expectedStatus = route == "/Home/ServiceUnavailable" ? 503 : route.Contains("reference=missing") ? 404 : 200;
                if (response?.Status != expectedStatus) failures.Add($"{route}: HTTP {response?.Status}, expected {expectedStatus}");
                if (page.Url.Contains("/Login") && !route.Contains("/Login")) failures.Add($"{route}: unexpected login redirect");
                if (route == "/Identity/Account/Register") await SignupTests.AcceptNotice(page);
                var problems = await LayoutProblems(page);
                failures.AddRange(problems.Select(p => route + ": " + p));
                routes.Add(route);
                if (problems.Length > 0 || (culture == "en" && device is "1440" or "small" or "iphone") || route is "/Patient/Home" or "/Patient/Encounters" or "/" or "/Malasakit/Navigator")
                    await page.ScreenshotAsync(new() { Path = Path.Combine(app.Artifacts, $"{engine}-{device}-{culture}-{Array.IndexOf(group, route)}-{(ReferenceEquals(group, PatientRoutes) ? "patient" : "public")}.png"), FullPage = true });
            }
        }
        await File.WriteAllLinesAsync(Path.Combine(app.Artifacts, $"{engine}-{device}-{culture}.txt"), new[] { "ENV " + environment }.Concat(routes.Select(r => "VISITED " + r)).Concat(failures).Concat(scriptErrors));
        output.WriteLine($"Visited {routes.Count} screens in {engine}/{device}/{culture}.");
        Assert.True(failures.Count == 0 && scriptErrors.Count == 0, string.Join(Environment.NewLine, failures.Concat(scriptErrors)));
    }

    [Fact]
    public async Task Desktop_account_menu_does_not_move_content()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("1440"));
        var page = await context.NewPageAsync();
        await PortalFixture.SignIn(page);
        var main = page.Locator("main");
        var before = (await main.BoundingBoxAsync())!.Y;
        var trigger = page.GetByRole(AriaRole.Button, new() { Name = "User Account Menu" });
        await trigger.ClickAsync();
        await Expect(page.Locator(".nav-account .dropdown-menu")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(app.Artifacts, "desktop-account-menu.png") });
        Assert.InRange(Math.Abs((await main.BoundingBoxAsync())!.Y - before), 0, 1);
        await trigger.PressAsync("ArrowDown");
        Assert.True(await page.Locator(".nav-account .dropdown-menu a").First.EvaluateAsync<bool>("el => el === document.activeElement"));
        await page.Keyboard.PressAsync("Escape");
        await Expect(trigger).ToBeFocusedAsync();
        await Expect(page.Locator(".nav-account .dropdown-menu")).ToBeHiddenAsync();
        await trigger.ClickAsync();
        await page.Locator("h1").ClickAsync();
        await Expect(page.Locator(".nav-account .dropdown-menu")).ToBeHiddenAsync();
        Assert.Equal(0, await page.Locator(".dashboard-hero a").CountAsync());
    }

    [Theory]
    [InlineData("webkit", "iphone")]
    [InlineData("chromium", "pixel")]
    [InlineData("chromium", "small")]
    public async Task Mobile_menu_and_record_metadata(string engine, string device)
    {
        await using var browser = await app.Engine(engine).LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context(device));
        var page = await context.NewPageAsync();
        await PortalFixture.SignIn(page);
        var metrics = await page.EvaluateAsync<string>("() => JSON.stringify({touch:navigator.maxTouchPoints, screen:screen.width, dpr:devicePixelRatio, coarse:matchMedia('(pointer:coarse)').matches})");
        Assert.True(await page.EvaluateAsync<bool>("() => (navigator.maxTouchPoints > 0 || matchMedia('(pointer:coarse)').matches) && screen.width < 500 && devicePixelRatio > 1"), metrics);
        var trigger = page.Locator(".mobile-menu-trigger");
        Assert.Equal("", (await trigger.InnerTextAsync()).Trim());
        var bounds = (await trigger.BoundingBoxAsync())!;
        Assert.True(bounds.Width >= 44 && bounds.Height >= 44);
        await trigger.TapAsync();
        var menu = page.Locator("#mobileUtilityMenu");
        await Expect(menu).ToBeVisibleAsync();
        await Expect(menu).ToHaveAttributeAsync("aria-modal", "true");
        await menu.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        await Expect(menu).ToBeHiddenAsync();
        await Expect(trigger).ToBeFocusedAsync();
        await page.Locator(".mobile-tab-bar a[href='/Patient/Encounters']").TapAsync();
        await Expect(page.Locator(".mobile-tab-bar [aria-current='page']")).ToHaveAttributeAsync("href", "/Patient/Encounters");
        var reference = page.Locator(".encounter-reference").First;
        Assert.True((await reference.BoundingBoxAsync())!.Width >= 200);
        Assert.True((await reference.BoundingBoxAsync())!.Height < 70);
        Assert.Empty(await LayoutProblems(page));
        await page.Locator(".mobile-tab-bar a[href='/Patient/Home']").TapAsync();
        var heading = page.Locator(".appointment-summary h2");
        Assert.True((await heading.BoundingBoxAsync())!.Width >= 180);
        Assert.True((await heading.BoundingBoxAsync())!.Height < 80);
    }
}
