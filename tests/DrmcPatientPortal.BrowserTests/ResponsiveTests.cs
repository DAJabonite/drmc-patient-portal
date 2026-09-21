using Microsoft.Playwright;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace DrmcPatientPortal.BrowserTests;

[Collection("Portal")]
public sealed class ResponsiveTests(PortalFixture app, ITestOutputHelper output)
{
    internal static IEnumerable<string> PublicScreens => PublicRoutes;
    internal static IEnumerable<string> PatientScreens => PatientRoutes;
    private static readonly string[] PublicRoutes = ["/", "/Directory", "/Directory/Doctor/1", "/Directory/Department?name=Internal%20Medicine", "/OpdGuide", "/Advisories", "/Advisories/Details?slug=dengue-4s-prevention-alert", "/Home/Privacy", "/Home/ServiceUnavailable", "/Malasakit", "/Malasakit/Program?code=MALASAKIT", "/Identity/Account/Login", "/Identity/Account/Register", "/Identity/Account/ForgotPassword", "/Identity/Account/AccessDenied", "/Home/Error"];
    private static readonly string[] PatientRoutes = ["/Patient/MedicalHistory", "/Patient/MedicalHistory?category=ADMITTED", "/Malasakit/Apply", "/Malasakit/Status", "/Patient/Home", "/Patient/Visits", "/Patient/Visits/Details/1", "/Patient/LabResults", "/Patient/LabResults/Details/1", "/Patient/Medications", "/Patient/Medications/Details/1", "/Patient/Audit", "/Identity/Account/Manage", "/Identity/Account/Manage/MyIds", "/Identity/Account/Manage/TwoFactorAuthentication", "/Identity/Account/Manage/EnableAuthenticator", "/Patient/Triage/Start/2", "/Patient/Triage/Summary/1", "/Patient/Triage/EmergencyWarning"];

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
                if (problems.Length > 0 || (culture == "en" && device is "1440" or "small" or "iphone") || route is "/Patient/Home" or "/Patient/Visits" or "/")
                {
                    // CSS pixels avoid WebKit's 32767-pixel image limit on high-DPI phones.
                    // Layout checks still inspect the entire page, including long audit histories.
                    var fullPage = await page.EvaluateAsync<bool>("() => document.documentElement.scrollHeight < 32000");
                    await page.ScreenshotAsync(new() { Path = Path.Combine(app.Artifacts, $"{engine}-{device}-{culture}-{Array.IndexOf(group, route)}-{(ReferenceEquals(group, PatientRoutes) ? "patient" : "public")}.png"), FullPage = fullPage, Scale = ScreenshotScale.Css });
                }
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
        var menu = page.Locator("#mobileUtilityMenu");
        await Expect(menu).ToBeVisibleAsync();
        await Expect(menu).ToHaveCSSAsync("transform", "none");
        await page.ScreenshotAsync(new() { Path = Path.Combine(app.Artifacts, "desktop-account-menu.png") });
        Assert.InRange(Math.Abs((await main.BoundingBoxAsync())!.Y - before), 0, 1);
        // Focus stays trapped inside the drawer while it is open.
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await page.EvaluateAsync<bool>("() => !!document.activeElement.closest('#mobileUtilityMenu')"));
        await page.Keyboard.PressAsync("Escape");
        await Expect(menu).ToBeHiddenAsync();
        await Expect(trigger).ToBeFocusedAsync();
        await trigger.ClickAsync();
        await Expect(menu).ToBeVisibleAsync();
        await page.Locator(".offcanvas-backdrop").ClickAsync();
        await Expect(menu).ToBeHiddenAsync();
        Assert.Equal(0, await page.Locator(".dashboard-hero a").CountAsync());
    }

    [Fact]
    public async Task Removed_queue_feature_is_not_linked_and_routes_return_not_found()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("small"));
        var page = await context.NewPageAsync();

        await page.GotoAsync("/");
        Assert.Equal(0, await page.Locator("a[href^='/Queue']").CountAsync());

        foreach (var route in new[] { "/Queue", "/Queue/Status?ticketNumber=IM-104", "/Queue/Live" })
        {
            var response = await page.GotoAsync(route);
            Assert.Equal(404, response?.Status);
        }
    }

    [Fact]
    public async Task Removed_malasakit_navigator_is_not_linked_and_routes_return_not_found()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("small"));
        var page = await context.NewPageAsync();

        foreach (var route in new[] { "/", "/Malasakit", "/Malasakit/Program?code=MALASAKIT" })
        {
            await page.GotoAsync(route);
            Assert.Equal(0, await page.Locator("a[href*='/Malasakit/Navigator'], form[action*='/Malasakit/Assess']").CountAsync());
        }

        foreach (var route in new[] { "/Malasakit/Navigator", "/Malasakit/Assess" })
        {
            var response = await page.GotoAsync(route);
            Assert.Equal(404, response?.Status);
        }
    }

    [Fact]
    public async Task Removed_print_features_are_not_linked_and_routes_return_not_found()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("small"));
        var page = await context.NewPageAsync();
        await PortalFixture.SignIn(page);

        foreach (var route in new[]
                 {
                     "/Patient/Visits",
                     "/Patient/Visits/Details/1",
                     "/Patient/LabResults",
                     "/Patient/LabResults/Details/1",
                     "/Patient/Audit",
                     "/Advisories/Details?slug=dengue-4s-prevention-alert"
                 })
        {
            var response = await page.GotoAsync(route);
            Assert.Equal(200, response?.Status);
            Assert.Equal(0, await page.Locator("a[href*='/Print'], button[onclick*='print'], button[onclick*='Print']").CountAsync());
        }

        foreach (var route in new[] { "/Patient/Visits/Print/1", "/Patient/LabResults/Print/1" })
        {
            var response = await page.GotoAsync(route);
            Assert.Equal(404, response?.Status);
        }
    }

    [Fact]
    public async Task Removed_biomarker_trends_are_not_linked_and_route_returns_not_found()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("small"));
        var page = await context.NewPageAsync();
        await PortalFixture.SignIn(page);

        foreach (var route in new[] { "/Patient/LabResults", "/Patient/LabResults/Details/1" })
        {
            var response = await page.GotoAsync(route);
            Assert.Equal(200, response?.Status);
            Assert.Equal(0, await page.Locator("a[href*='/LabResults/Trends'], a[href*='/Patient/LabResults/Trends']").CountAsync());
            await Expect(page.GetByText("View Biomarker Trends", new() { Exact = true })).ToHaveCountAsync(0);
            await Expect(page.GetByText("Track Trend", new() { Exact = true })).ToHaveCountAsync(0);
        }

        var removedRoute = await page.GotoAsync("/Patient/LabResults/Trends");
        Assert.Equal(404, removedRoute?.Status);
    }

    [Fact]
    public async Task Lab_results_hide_sensitive_values_and_display_claiming_guidance()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("small"));
        var page = await context.NewPageAsync();
        await PortalFixture.SignIn(page);

        // Index page
        await page.GotoAsync("/Patient/LabResults");
        await Expect(page.GetByText("Patient Health Data Privacy Notice")).ToHaveCountAsync(0);
        await Expect(page.Locator("text=Ready for Claiming").First).ToBeVisibleAsync();
        await Expect(page.Locator("text=Claiming Unit:").First).ToBeVisibleAsync();

        // Details page
        await page.GotoAsync("/Patient/LabResults/Details/1");
        await Expect(page.GetByText("Result Availability & Claiming Guide")).ToBeVisibleAsync();
        await Expect(page.GetByText("Where & How to Claim Your Official Results")).ToBeVisibleAsync();
        await Expect(page.GetByText("Releasing Location")).ToBeVisibleAsync();
        await Expect(page.GetByText("Claiming Requirements")).ToBeVisibleAsync();

        // Privacy assertion: NO numerical analyte breakdown table or clinical notes
        await Expect(page.Locator("table")).ToHaveCountAsync(0);
        await Expect(page.GetByText("Quantitative Analyte Breakdown")).ToHaveCountAsync(0);
        await Expect(page.GetByText("Biological Reference Ranges")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Mobile_filter_navigation_uses_dropdowns_instead_of_scrolling_pills()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var mobileContext = await browser.NewContextAsync(app.Context("small"));
        var mobilePage = await mobileContext.NewPageAsync();
        await PortalFixture.SignIn(mobilePage);

        var filters = new[]
        {
            (Route: "/Patient/LabResults", SelectId: "#labCategoryFilter", Parameter: "category"),
            (Route: "/Advisories", SelectId: "#advisoryCategoryFilter", Parameter: "category"),
            (Route: "/Directory", SelectId: "#directoryDepartmentFilter", Parameter: "department"),
            (Route: "/Patient/Visits", SelectId: "#encounterDepartmentFilter", Parameter: "department")
        };

        foreach (var filter in filters)
        {
            await mobilePage.GotoAsync(filter.Route);
            var select = mobilePage.Locator(filter.SelectId);
            await Expect(select).ToBeVisibleAsync();
            await Expect(mobilePage.Locator(".desktop-filter-pills")).ToBeHiddenAsync();
            await select.SelectOptionAsync(new SelectOptionValue { Index = 1 });
            await mobilePage.WaitForURLAsync(url => url.Contains($"{filter.Parameter}="));
            Assert.Empty(await LayoutProblems(mobilePage));
        }

        await using var desktopContext = await browser.NewContextAsync(app.Context("1440"));
        var desktopPage = await desktopContext.NewPageAsync();
        await PortalFixture.SignIn(desktopPage);

        foreach (var filter in filters)
        {
            await desktopPage.GotoAsync(filter.Route);
            if (filter.Route != "/Advisories")
            {
                await Expect(desktopPage.Locator(filter.SelectId)).ToBeVisibleAsync();
                if (filter.Route == "/Patient/Visits")
                    await Expect(desktopPage.Locator("#encounterDateFilter")).ToBeVisibleAsync();
                if (filter.Route == "/Patient/LabResults")
                    await Expect(desktopPage.Locator("#labDateFilter")).ToBeVisibleAsync();
            }
            else
            {
                await Expect(desktopPage.Locator(filter.SelectId)).ToBeHiddenAsync();
                await Expect(desktopPage.Locator(".desktop-filter-pills")).ToBeVisibleAsync();
            }
        }
    }

    [Fact]
    public async Task Page_headers_do_not_use_decorative_label_badges()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("small"));
        var page = await context.NewPageAsync();
        await PortalFixture.SignIn(page);

        var labelsByRoute = new Dictionary<string, string>
        {
            ["/Patient/Visits"] = "Electronic Medical Record",
            ["/Patient/LabResults"] = "Patient Health Records",
            ["/Patient/Medications"] = "Pharmacy Health Record",
            ["/Patient/Audit"] = "Data Privacy & Security",
            ["/Identity/Account/Manage"] = "Patient Account Security",
            ["/OpdGuide"] = "Patient Orientation",
            ["/Advisories"] = "Official Announcements",
            ["/Malasakit"] = "Republic Act No. 11463",
            ["/Directory"] = "Medical Staff Directory"
        };

        foreach (var (route, label) in labelsByRoute)
        {
            var response = await page.GotoAsync(route);
            Assert.Equal(200, response?.Status);
            await Expect(page.Locator(".page-header .badge").Filter(new() { HasText = label })).ToHaveCountAsync(0);
        }
    }

    [Fact]
    public async Task Desktop_menu_opens_the_same_utility_drawer_as_mobile()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("1440"));
        var page = await context.NewPageAsync();
        await PortalFixture.SignIn(page);
        var trigger = page.Locator(".nav-menu-trigger");
        await Expect(trigger).ToBeVisibleAsync();
        await Expect(page.Locator(".nav-menu-trigger")).ToHaveCountAsync(1); // one shared menu trigger, no duplicate menus
        await Expect(trigger).ToHaveAttributeAsync("aria-label", "User Account Menu");
        var viewport = page.ViewportSize!;
        await trigger.ClickAsync();
        var menu = page.Locator("#mobileUtilityMenu");
        await Expect(menu).ToBeVisibleAsync();
        await Expect(menu).ToHaveCSSAsync("transform", "none"); // wait out the slide-in transition
        var menuBox = (await menu.BoundingBoxAsync())!;
        Assert.True(menuBox.Width <= 360 && Math.Abs(menuBox.X + menuBox.Width - viewport.Width) <= 1, $"Drawer box {menuBox.Width}x@{menuBox.X} in {viewport.Width}px viewport");
        await Expect(menu).ToHaveAttributeAsync("aria-modal", "true");
        await Expect(menu.Locator("#mobileHospitalServicesHeading")).ToBeVisibleAsync();
        await Expect(menu.Locator(".mobile-more-link[href='/Directory']")).ToBeVisibleAsync();
        await Expect(menu.Locator(".mobile-more-link[href='/Patient/MedicalHistory']")).ToHaveCountAsync(0);
        await Expect(menu.GetByRole(AriaRole.Link, new() { Name = "Two-Factor Auth (2FA)" })).ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(app.Artifacts, "desktop-menu-drawer.png") });
        await menu.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        await Expect(menu).ToBeHiddenAsync();
        await Expect(trigger).ToBeFocusedAsync();
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
        await page.Locator(".mobile-tab-bar a[href='/Patient/Visits']").TapAsync();
        await Expect(page.Locator(".mobile-tab-bar [aria-current='page']")).ToHaveAttributeAsync("href", "/Patient/Visits");
        var reference = page.Locator(".encounter-reference").First;
        Assert.True((await reference.BoundingBoxAsync())!.Width >= 200);
        Assert.True((await reference.BoundingBoxAsync())!.Height < 70);
        Assert.Empty(await LayoutProblems(page));
        await page.Locator(".mobile-tab-bar a[href='/Patient/Home']").TapAsync();
        var heading = page.Locator(".summary-card h2").First;
        Assert.True((await heading.BoundingBoxAsync())!.Width >= 40);
        Assert.True((await heading.BoundingBoxAsync())!.Height < 80);
    }

    [Fact]
    public async Task Appointments_routes_and_ui_are_retired_and_hidden()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("1440"));
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync("/Appointments/Book");
        Assert.Equal(404, response?.Status);

        await page.GotoAsync("/");
        await Expect(page.Locator("a[href*='/Appointments']")).ToHaveCountAsync(0);

        await page.GotoAsync("/Directory");
        await Expect(page.Locator("a[href*='/Appointments']")).ToHaveCountAsync(0);

        await PortalFixture.SignIn(page);
        await page.GotoAsync("/Patient/Home");
        await Expect(page.Locator("a[href*='/Appointments']")).ToHaveCountAsync(0);
        await Expect(page.Locator(".appointment-summary")).ToHaveCountAsync(0);
    }
}
