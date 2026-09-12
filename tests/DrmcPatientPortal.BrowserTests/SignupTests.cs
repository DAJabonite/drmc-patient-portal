using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace DrmcPatientPortal.BrowserTests;

[Collection("Portal")]
public sealed class SignupTests(PortalFixture app)
{
    internal static async Task AcceptNotice(IPage page)
    {
        await Expect(page.Locator("#registrationConsent")).ToBeVisibleAsync();
        await page.Locator("#consentDocument").PressAsync("Control+End");
        await Expect(page.Locator("#regConsent")).ToBeEnabledAsync();
        if (await page.EvaluateAsync<bool>("() => matchMedia('(pointer:coarse)').matches"))
        {
            await page.Locator("label[for=regConsent]").TapAsync();
            await page.Locator("#acceptConsent").TapAsync();
        }
        else
        {
            await page.Locator("#regConsent").CheckAsync();
            await page.Locator("#acceptConsent").ClickAsync();
        }
        await Expect(page.Locator("#registrationConsent")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData("chromium", "1440")]
    [InlineData("chromium", "1920")]
    [InlineData("chromium", "768")]
    [InlineData("chromium", "pixel")]
    [InlineData("chromium", "small")]
    [InlineData("webkit", "iphone")]
    [InlineData("webkit", "landscape")]
    [InlineData("webkit", "1440")]
    [InlineData("firefox", "1440")]
    public async Task Signup_gate_stepper_buttons_and_inline_validation(string engine, string device)
    {
        await using var browser = await app.Engine(engine).LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context(device));
        var page = await context.NewPageAsync();
        var dialogs = new List<string>();
        page.Dialog += async (_, dialog) => { dialogs.Add(dialog.Message); await dialog.DismissAsync(); };
        await page.GotoAsync("/Identity/Account/Register");
        await Expect(page.Locator("#registrationConsent")).ToBeVisibleAsync();
        await Expect(page.Locator("#consentDocument")).ToBeFocusedAsync();
        await Expect(page.Locator("#regConsent")).ToBeDisabledAsync();
        await Expect(page.Locator("#acceptConsent")).ToBeDisabledAsync();
        await Expect(page.Locator("#btnManualSkip")).ToBeDisabledAsync();
        await page.Locator("#consentDocument").EvaluateAsync("el => { el.scrollTop = (el.scrollHeight - el.clientHeight) / 2; el.dispatchEvent(new Event('scroll')); }");
        await Expect(page.Locator("#regConsent")).ToBeDisabledAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#registrationConsent")).ToBeVisibleAsync();
        // Keyboard users remain within the modal, including when wrapping backwards.
        await page.Locator("#leaveRegistration").FocusAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        Assert.True(await page.EvaluateAsync<bool>("() => !!document.activeElement.closest('#registrationConsent')"));
        await page.Locator("#consentDocument").PressAsync("Home");
        var bounds = await page.Locator("#registrationConsent .modal-content").BoundingBoxAsync();
        Assert.True(bounds!.Y >= 0 && bounds.Y + bounds.Height <= page.ViewportSize!.Height + 1);
        Assert.True((await page.Locator("#consentDocument").BoundingBoxAsync())!.Height >= 60, "Notice must retain readable scroll space.");
        if (device is "1440" or "small" or "iphone")
            await page.ScreenshotAsync(new() { Path = Path.Combine(app.Artifacts, $"signup-{engine}-{device}-notice.png") });
        if (engine == "chromium" && device is "1440" or "small") await Accessible(page);
        await AcceptNotice(page);
        await Expect(page.Locator("#wizardStep1 h2")).ToBeFocusedAsync();
        var initialStyle = await page.Locator("#stepBtn1").EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        await CheckStep(page, engine, device, 1);
        await page.Locator("#selectIdType").SelectOptionAsync("Philippine Passport");
        await page.Locator("#btnGoToCapture").ClickAsync();
        await CheckStep(page, engine, device, 2);
        await Expect(page.Locator("#btnTriggerOcr")).ToBeDisabledAsync();
        await page.Locator("#btnBackToStep1").ClickAsync();
        Assert.Equal(initialStyle, await page.Locator("#stepBtn1").EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor"));
        await page.Locator("#btnManualSkip").ClickAsync();
        await Expect(page.Locator("#stepLabel2")).ToHaveTextAsync("Photo skipped");
        await page.Locator("#btnGoToStep4").ClickAsync();
        await Expect(page.Locator("#txtFirstName")).ToBeFocusedAsync();
        await Expect(page.Locator("[data-valmsg-for='Input.FirstName']")).ToContainTextAsync("first name");
        await Expect(page.Locator("[data-valmsg-for='Input.LastName']")).ToContainTextAsync("last name");
        await CheckStep(page, engine, device, 3);
        await FillDetails(page);
        await page.Locator("#txtIdNumber").PressAsync("Enter");
        await CheckStep(page, engine, device, 4);
        await page.Locator("#registerSubmit").ClickAsync();
        await Expect(page.Locator("input[name='Input.Email']")).ToBeFocusedAsync();
        await Expect(page.Locator("[data-valmsg-for='Input.Email']")).Not.ToBeEmptyAsync();
        await page.Locator("#regPassword").FillAsync("Example!2026");
        await page.Locator("[aria-controls='regPassword']").ClickAsync();
        await Expect(page.Locator("#regPassword")).ToHaveAttributeAsync("type", "text");
        await page.Locator("#reviewConsent").ClickAsync();
        await Expect(page.Locator("#regConsent")).ToBeCheckedAsync();
        await page.Locator("#regConsent").UncheckAsync();
        await Expect(page.Locator("#acceptConsent")).ToBeDisabledAsync();
        await page.Locator("#regConsent").CheckAsync();
        await page.Locator("#acceptConsent").ClickAsync();
        await Expect(page.Locator("#wizardStep4 h2")).ToBeFocusedAsync();
        Assert.Empty(dialogs);
    }

    private async Task CheckStep(IPage page, string engine, string device, int step)
    {
        await Expect(page.Locator($"#wizardStep{step}")).ToBeVisibleAsync();
        await Expect(page.Locator("#wizardStepperNav [aria-current='step']")).ToHaveCountAsync(1);
        Assert.Empty(await ResponsiveTests.LayoutProblems(page));
        if (device is "1440" or "1920")
        {
            foreach (var button in await page.Locator($"#wizardStep{step} .registration-actions .btn").AllAsync())
            {
                var box = await button.BoundingBoxAsync();
                Assert.InRange(box!.Height, 44, 60); // No tall multi-line desktop actions.
            }
        }
        if (device is "1440" or "small" or "iphone")
        {
            // WebKit can scroll the focused field into view when taking a full-page image.
            // Focus assertions above are already complete; capture the stable top-of-page layout.
            await page.EvaluateAsync("() => { document.activeElement.blur(); window.scrollTo({ top: 0, behavior: 'instant' }); }");
            await page.ScreenshotAsync(new() { Path = Path.Combine(app.Artifacts, $"signup-{engine}-{device}-step{step}.png"), FullPage = true });
        }
        if (engine == "chromium" && device is "1440" or "small") await Accessible(page);
    }

    private static async Task Accessible(IPage page)
    {
        var result = await page.RunAxe(new AxeRunOptions { RunOnly = new RunOnlyOptions
            { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] } });
        Assert.True(result.Violations.Length == 0, string.Join("\n", result.Violations.SelectMany(v => v.Nodes.Select(n => v.Id + ": " + n.Html))));
    }

    private static async Task FillDetails(IPage page)
    {
        await page.Locator("#txtFirstName").FillAsync("Signup");
        await page.Locator("#txtLastName").FillAsync("Testpatient");
        await page.Locator("#txtIdNumber").FillAsync("TEST-SIGNUP-01");
    }

    [Fact]
    public async Task Scan_failure_loading_and_server_rejection_recover_inline()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("1440"));
        var page = await context.NewPageAsync();
        var dialogs = new List<string>();
        page.Dialog += async (_, dialog) => { dialogs.Add(dialog.Message); await dialog.DismissAsync(); };
        await page.GotoAsync("/Identity/Account/Register?ReturnUrl=%2FPatient%2FHome");
        await AcceptNotice(page);
        await page.Locator("#selectIdType").SelectOptionAsync("Philippine Passport");
        await page.Locator("#btnGoToCapture").ClickAsync();
        await page.Locator("#frontPhotoInput").SetInputFilesAsync(new FilePayload { Name = "synthetic.png", MimeType = "image/png",
            Buffer = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a4x8AAAAASUVORK5CYII=") });
        var release = new TaskCompletionSource();
        await page.RouteAsync("**/*handler=ExtractId", async route => { await release.Task; await route.FulfillAsync(new() { Status = 503, Body = "Unavailable" }); });
        await page.Locator("#btnTriggerOcr").ClickAsync();
        await Expect(page.Locator("#ocrProcessingState")).ToBeVisibleAsync();
        await Expect(page.Locator("#btnManualSkipFromStep2")).ToBeDisabledAsync();
        release.SetResult();
        await Expect(page.Locator("#stepFeedback3")).ToContainTextAsync("could not scan");
        await Expect(page.Locator("#autofillNotice")).ToBeHiddenAsync();
        await FillDetails(page);
        await page.Locator("#btnGoToStep4").ClickAsync();
        await page.Locator("input[name='Input.Email']").FillAsync("patient@drmc.doh.gov.ph");
        await page.Locator("input[name='Input.Mobile']").FillAsync("9175550123");
        await page.Locator("#regPassword").FillAsync("Signup!2026Test");
        await page.Locator("#regConfirmPassword").FillAsync("Signup!2026Test");
        await page.Locator("#registerSubmit").ClickAsync();
        await Expect(page.Locator("#registrationErrors")).ToContainTextAsync("already taken");
        await Expect(page.Locator("#wizardStep4")).ToBeVisibleAsync();
        await Expect(page.Locator("#registrationErrors")).ToBeFocusedAsync();
        await Expect(page.Locator("#registrationConsent")).ToBeHiddenAsync();
        await Expect(page.Locator("#registerSubmit")).ToBeEnabledAsync();
        await Expect(page.Locator("#registerForm")).ToHaveAttributeAsync("action", "/Identity/Account/Register?returnUrl=%2FPatient%2FHome");
        Assert.Empty(dialogs);
    }

    [Fact]
    public async Task Consent_is_required_on_server_and_declining_leaves_signup()
    {
        await using var browser = await app.Playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(app.Context("small"));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/Identity/Account/Register");
        var token = await page.Locator("#registerForm input[name=__RequestVerificationToken]").InputValueAsync();
        var data = context.APIRequest.CreateFormData();
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Input.PrivacyConsent"] = "false", ["Input.FirstName"] = "No",
            ["Input.LastName"] = "Consent", ["Input.IdType"] = "Philippine Passport", ["Input.IdNumber"] = "TEST-NO-CONSENT",
            ["Input.Email"] = $"no-consent-{Guid.NewGuid():N}@example.test", ["Input.Mobile"] = "9175550123",
            ["Input.Password"] = "Signup!2026Test", ["Input.ConfirmPassword"] = "Signup!2026Test", ["Input.IsManualEntry"] = "true"
        }) data.Set(key, value);
        var response = await context.APIRequest.PostAsync("/Identity/Account/Register", new() { Form = data });
        Assert.Contains("Read and accept the privacy notice", await response.TextAsync());
        await page.Locator("#leaveRegistration").ClickAsync();
        await page.WaitForURLAsync(app.Url + "/");
        await page.GotoAsync("/Identity/Account/Register");
        await Expect(page.Locator("#regConsent")).ToBeDisabledAsync();
        await Expect(page.Locator("#regConsent")).Not.ToBeCheckedAsync();
    }
}
