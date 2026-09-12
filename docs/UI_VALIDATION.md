# Desktop and mobile UI validation

Validation date: 2026-09-13. Branch: `codex/ui-ux-validation-fixes`, based on `origin/master` at `293d07e`.

The subsequent [signup follow-up](SIGNUP_VALIDATION.md) adds a privacy/terms entry dialog and fixes all signup steps. Its latest full run passes **42 browser tests and 25 backend tests**. The initial audit results below are retained as their original evidence.

## Reproduce

```powershell
pwsh scripts/test-ui.ps1
# After installing the browser binaries once:
pwsh scripts/test-ui.ps1 -SkipBrowserInstall
```

Requires .NET 10 and PowerShell. Playwright and axe-core are **test-only** dependencies. The script builds Release, installs Chromium/Firefox/WebKit, and runs the existing unit tests and the separate xUnit browser project. Each browser run starts a loopback server with a unique temporary SQLite database, document directories, and encryption keys. The fixture seeds synthetic development patients, adds a deterministic triage summary, and removes its temporary state after the run. It does not connect to the normal development database or external notification services.

Results, screenshots, route logs, browser metrics, and accessibility reports are generated under ignored `output/playwright/`. TRX files are under `output/playwright/test-results/`. A failing check makes the script exit unsuccessfully.

## Verified fixes

| Defect | Change and regression evidence |
| --- | --- |
| Opening the desktop account menu added about 330px to the navigation height | Position the dropdown as an overlay; assert main-content displacement is at most 1px; check arrow navigation, Escape/focus restoration, and outside dismissal. |
| Visible “Menu” text consumed phone header space | Icon-only 44×44px trigger, retaining localized accessible name and offcanvas controls; exercise taps in fresh mobile contexts. |
| Access history and Settings appeared twice on the dashboard | Remove welcome-banner duplicates; keep shortcuts and account/utility access. |
| Appointment titles and encounter references collapsed into narrow columns | Reflow heading/status, wrap metadata, and give references a separate phone row; assert readable geometry. |
| Dashboard cards became cramped at 992–1024px | Use three columns from the extra-large breakpoint; retain wider rows below it. |
| Long specialties, print headings, and form actions overflowed or broke words | Allow badge wrapping and flexible card children; wrap action groups and reduce excessive phone button padding. Check element bounds and mid-word line breaks, not only document width. |
| Laboratory search and several form labels lacked usable accessible names | Bound the search width, name its control/button, and connect labels to custom input IDs in login, registration, booking, and triage. |
| Footer and semantic status text had insufficient contrast | Remove the obsolete scoped link-color override; darken semantic text on pale surfaces and secondary-outline button text. |
| Related advisories had unnamed stretched links | Give links the associated advisory title as their accessible name. |
| Wide record tables were not keyboard-scrollable | Add named focusable regions to laboratory measurements and access history. |
| Fixed phone bars interfered with scroll-to-control behavior | Reserve scroll padding and control scroll margins; retain safe-area-aware document padding. |
| Invalid check-in links rendered blank browser error pages | Use the same generic recovery view for missing and inaccessible references, retaining HTTP 404 and ownership/capability checks. |
| Cancelled appointments retained “Confirmed” presentation | Confirmation and check-in views display the stored status; cancellation tests verify the stale success label is absent. |

Routes, form handlers, authentication, clinical data, database schema, and production dependencies remain intact. The only controller change supplies an HTML recovery body for the existing check-in 404 response.

## Coverage

The layout matrix visits 42 screens per profile: **756 screen visits across 18 browser/device/language profiles**, plus interaction scenarios and automated accessibility checks on 84 desktop/mobile screen states.

| Browser/profile | Configuration |
| --- | --- |
| Chromium desktop | 1440×900 and 1920×1080 |
| Chromium tablet/breakpoints | 768×1024, 991×1024, 992×1024, 1024×1024 |
| Chromium Pixel 5 | Device descriptor: 393×727 viewport, 393×851 screen, DPR 2.75, mobile user agent and touch |
| Chromium small phone | 320×568 viewport and screen, DPR 2, mobile user agent and touch |
| WebKit iPhone 13 | Device descriptor: 390×664 viewport, 390×844 screen, DPR 3, mobile user agent and coarse pointer/touch input |
| WebKit iPhone landscape | iPhone 13 landscape descriptor |
| Firefox and WebKit desktop | 1440×900 |
| Filipino and Cebuano | Each runs on Chromium desktop, Chromium 320px phone, and WebKit iPhone |

Mobile contexts are newly created with Playwright device descriptors, including screen, viewport, scale, mobile behavior, and touch configuration. They are not resized desktop sessions. On Windows WebKit, `navigator.maxTouchPoints` reports zero despite the coarse-pointer/mobile configuration; the suite verifies that configuration and performs actual Playwright tap interactions. Device metrics are recorded in each route log.

Screen inventory:

- Public: home; directory/list/physician/department; OPD guide; advisories/list/detail; queue/list/ticket/missing ticket; privacy; service-unavailable and error pages; Malasakit/list/program/navigator; booking; unavailable check-in; login; registration; forgot-password; access-denied.
- Signed in: dashboard; encounter list/detail/print; laboratory list/detail/print/trends; medication list/detail/refill status; audit; profile; 2FA settings/setup; appointment confirmation/check-in; triage form/summary/emergency warning.
- Interactions: guest return URL, empty accounts, search, language persistence, profile save, booking validation/submission/check-in/cancellation, routine and emergency triage, assistance questionnaire/results, refill submission, queue refresh success/failure, registration capture-step/manual-step navigation and validation, authenticator enrollment and 2FA sign-in, dropdown/offcanvas behavior, keyboard skip link, print chrome, and effective 200% desktop reflow with reduced motion.

## Results and evidence

Final combined run (`pwsh scripts/test-ui.ps1 -SkipBrowserInstall`): **passed**.

- Release build: **0 warnings, 0 errors**.
- Existing backend tests: **25 passed, 0 failed, 0 skipped**.
- Browser tests: **31 passed, 0 failed, 0 skipped** (about 2 minutes 34 seconds).
- All 18 layout profiles completed all 42 routes without detected overflow, clipping, split heading/button words, unexpected status/redirects, or uncaught page-script errors.
- Automated accessibility: **0 violations across 84 screen states**. Separately recorded `incomplete` checks remain subject to the limits below.
- Local TRX evidence: `User_ANGELO_2026-09-13_01_44_11_net10.0.trx` (backend) and `User_ANGELO_2026-09-13_01_44_16_net10.0.trx` (browser).

The checked-in screenshots use synthetic development records:

| Before | After |
| --- | --- |
| [Desktop menu](screenshots/ui-validation/before-desktop-menu.png) | [Desktop menu](screenshots/ui-validation/after-desktop-menu.png) |
| [320px dashboard](screenshots/ui-validation/before-mobile-dashboard.png) | [320px dashboard](screenshots/ui-validation/after-mobile-dashboard.png) |
| [320px encounters](screenshots/ui-validation/before-mobile-encounters.png) | [320px encounters](screenshots/ui-validation/after-mobile-encounters.png) |

Additional examples: [iPhone dashboard](screenshots/ui-validation/after-iphone-dashboard.png), [320px print preview](screenshots/ui-validation/after-mobile-print.png).

## Limits

- Mobile results are **device emulation**, not physical iOS/Android certification. Physical camera capture, virtual-keyboard behavior, and real assistive-technology sessions were not exercised.
- The 200% check uses the equivalent CSS layout viewport and device scale; it does not claim an operating-system browser zoom interaction.
- axe checks the WCAG 2 A/AA, 2.1 A/AA, and 2.2 AA rules it can automate. Image-backed/gradient contrast, clipping in scroll regions, and some native-control cases produce `incomplete` results requiring human review. These are saved separately; a zero-violation result is not a claim of complete WCAG certification.
- Visual review includes the supplied defects, desktop account navigation in the in-app browser, and representative mobile record, profile, form, and print screenshots. Automated geometry and accessibility checks cover the full inventory. Fixed navigation appears at the initial viewport position in full-page screenshots; content remains scrollable beneath it.
- Registration exercises capture UI and manual entry, not a physical ID or camera/OCR integration. The existing backend/OCR tests run separately. No production SMS/email, HIS, or pharmacy integration is claimed.
