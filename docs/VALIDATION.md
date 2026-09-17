# Validation report

Consolidated record of UI, accessibility, and signup validation for the DRMC Patient Portal. This file replaces the former `UI_VALIDATION.md` and `SIGNUP_VALIDATION.md`.

For scope and requirement sign-off, see [`CLIENT_REQUIREMENTS_2026-09-13.md`](CLIENT_REQUIREMENTS_2026-09-13.md). For security posture, see [`SECURITY_REVIEW_TODO.md`](SECURITY_REVIEW_TODO.md).

---

## Current status

| Run | Backend tests | Browser tests | Build |
|---|---|---|---|
| September 13 client update (**latest**) | 34 passed | 46 passed | 0 warnings, 0 errors |
| Signup follow-up | 25 passed | 42 passed | 0 warnings, 0 errors |
| Initial desktop/mobile audit | 25 passed | 31 passed | 0 warnings, 0 errors |

The latest run adds medical history, other government IDs, Malasakit applications, database-upgrade verification, and the expanded responsive/accessibility matrix. NuGet reported no known vulnerable packages.

> These are recorded local runs, not CI results. There is no GitHub Actions workflow in this repository — re-run the commands below to confirm current numbers.

---

## How to reproduce

```powershell
pwsh scripts/test-ui.ps1
# After installing the browser binaries once:
pwsh scripts/test-ui.ps1 -SkipBrowserInstall
```

Requires .NET 10 and PowerShell 7+. Playwright and axe-core are **test-only** dependencies.

The script builds Release, installs Chromium/Firefox/WebKit, and runs both the unit-test project and the separate browser project. Each browser run starts a loopback server with its own temporary SQLite database, document directories, and encryption keys. The fixture seeds synthetic development patients, adds a deterministic triage summary, and removes its temporary state afterward. **It never touches the normal development database or any external notification service.**

Results, screenshots, route logs, browser metrics, and accessibility reports are written to the git-ignored `output/playwright/`; TRX files land in `output/playwright/test-results/`. A failing check makes the script exit unsuccessfully.

---

## Coverage

The layout matrix visits 40 screens per profile: **720 screen visits across 18 browser/device/language profiles**, plus interaction scenarios and automated accessibility checks.

| Browser / profile | Configuration |
|---|---|
| Chromium desktop | 1440×900 and 1920×1080 |
| Chromium tablet / breakpoints | 768×1024, 991×1024, 992×1024, 1024×1024 |
| Chromium Pixel 5 | 393×727 viewport, 393×851 screen, DPR 2.75, mobile UA and touch |
| Chromium small phone | 320×568 viewport and screen, DPR 2, mobile UA and touch |
| WebKit iPhone 13 | 390×664 viewport, 390×844 screen, DPR 3, mobile UA, coarse pointer/touch |
| WebKit iPhone landscape | iPhone 13 landscape descriptor |
| Firefox and WebKit desktop | 1440×900 |
| Filipino and Cebuano | Each on Chromium desktop, Chromium 320px phone, and WebKit iPhone |

Mobile contexts are created with Playwright device descriptors (screen, viewport, scale, mobile behavior, touch) — they are **not** resized desktop sessions. On Windows WebKit, `navigator.maxTouchPoints` reports zero despite the coarse-pointer configuration; the suite verifies the configuration and performs real tap interactions. Device metrics are recorded in each route log.

### Screen inventory

- **Public:** home; directory list/physician/department; OPD guide; advisories list/detail; privacy; service-unavailable and error pages; Malasakit list/program/navigator; login; registration; forgot-password; access-denied.
- **Signed in:** dashboard; encounter list/detail; laboratory list/detail; medication list/detail; audit; profile; 2FA settings/setup; triage form/summary/emergency warning.
- **Interactions:** guest return URL, empty accounts, search, language persistence, profile save, routine and emergency triage, assistance questionnaire/results, registration capture-step and manual-step navigation and validation, authenticator enrollment and 2FA sign-in, dropdown/offcanvas behavior, keyboard skip link, and effective 200% desktop reflow with reduced motion.

> Some screens exercised in earlier runs (appointment booking, confirmation, check-in, cancellation, refill submission, OPD queue) belong to features that have since been retired. See `README.md` for the current route map.

---

## Results

Final combined run (`pwsh scripts/test-ui.ps1 -SkipBrowserInstall`): **passed**.

- All 18 layout profiles completed every route without detected overflow, clipping, split heading/button words, unexpected status codes or redirects, or uncaught page-script errors.
- Automated accessibility: **0 violations across 84 screen states**, plus 10 dedicated signup scans. Separately recorded `incomplete` checks remain subject to the limits below.
- TRX evidence is generated locally under `output/playwright/test-results/`.

### Reference screenshots

All screenshots use synthetic development records.

| Before | After |
|---|---|
| [Desktop menu](screenshots/ui-validation/before-desktop-menu.png) | [Desktop menu](screenshots/ui-validation/after-desktop-menu.png) |
| [320px dashboard](screenshots/ui-validation/before-mobile-dashboard.png) | [320px dashboard](screenshots/ui-validation/after-mobile-dashboard.png) |
| [320px encounters](screenshots/ui-validation/before-mobile-encounters.png) | [320px encounters](screenshots/ui-validation/after-mobile-encounters.png) |

Also: [iPhone dashboard](screenshots/ui-validation/after-iphone-dashboard.png) · [desktop notice](screenshots/signup/desktop-notice.png) · [320px notice](screenshots/signup/mobile-notice.png) · signup [ID selection](screenshots/signup/desktop-step1.png), [photo actions](screenshots/signup/desktop-step2.png), [inline errors](screenshots/signup/desktop-step3.png), [credentials](screenshots/signup/desktop-step4.png) · [iPhone inline errors](screenshots/signup/iphone-step3.png).

---

## Defects fixed during validation

### Layout and accessibility

| Defect | Change and regression evidence |
|---|---|
| Opening the desktop account menu added about 330px to navigation height | Dropdown positioned as an overlay; main-content displacement asserted at ≤1px; arrow navigation, Escape/focus restoration, and outside dismissal checked |
| Visible “Menu” text consumed phone header space | Icon-only 44×44px trigger retaining the localized accessible name and offcanvas controls |
| Access history and Settings appeared twice on the dashboard | Welcome-banner duplicates removed; shortcuts and account access retained |
| Encounter references collapsed into narrow columns | Heading/status reflow, wrapped metadata, separate phone row for references |
| Dashboard cards were cramped at 992–1024px | Three columns from the extra-large breakpoint; wider rows below it |
| Long specialties and form actions overflowed or broke mid-word | Flexible card children, wrapped action groups, reduced phone button padding; element bounds asserted, not just document width |
| Laboratory search and several form labels lacked accessible names | Bounded search width, named control/button, labels bound to custom input IDs |
| Footer and semantic status text had insufficient contrast | Obsolete scoped link-color override removed; semantic text darkened on pale surfaces |
| Related advisories had unnamed stretched links | Links given the advisory title as accessible name |
| Wide record tables were not keyboard-scrollable | Named focusable regions added to laboratory measurements and access history |
| Fixed phone bars interfered with scroll-to-control behavior | Scroll padding and control scroll margins reserved; safe-area-aware document padding retained |

### Signup flow

- The four-step indicator renders consistently from first paint with equal-width columns; completed steps show a check, the current step carries `aria-current`, and manual entry marks the photo step as skipped. Circles convey progress rather than acting as dead buttons.
- Redundant “Step X of 4” badges removed; the shared indicator and section headings carry the context.
- All step actions share a 48px minimum height. Desktop actions fit one line; phone actions stack. The photo step keeps Back separate from Manual Entry and Scan ID.
- All registration JavaScript alerts replaced by inline feedback: required fields show their own messages, the first invalid field takes focus, native popups are disabled while client and server validation stay active, Enter advances the review step, and duplicate submissions are prevented.
- Scan failures no longer claim autofill succeeded; the scan state disables conflicting controls and restores them after the response. Upload actions support keyboard activation.
- Server rejection reopens the correct step, preserves the return URL, and focuses the server error or invalid field.
- Signup opens with a privacy-and-terms dialog before form controls become usable. The agreement checkbox stays disabled until the scroll region reaches the bottom; the user must then explicitly check it and select **Agree and continue**. **Leave signup** returns to public home without acceptance. Keyboard Home/End, focus containment, and focus restoration are covered.
- The dialog and the public privacy page share the same privacy notice and portal-use terms; the footer Terms of Use link points to the terms section.

The server-enforced `PrivacyConsent` field remains the stored consent value. **No consent-version audit record was added, and scrolling is a UI requirement, not proof that anyone read or understood the text.** No production dependencies were added; routes, form handlers, authentication, clinical data, and database schema were unchanged by this work.

---

## Limits — read before citing these results

- Mobile results are **device emulation**, not physical iOS/Android certification. Physical camera capture, virtual-keyboard behavior, and real assistive-technology sessions were not exercised.
- The 200% check uses the equivalent CSS layout viewport and device scale; it does not claim an operating-system browser-zoom interaction.
- axe covers the WCAG 2 A/AA, 2.1 A/AA, and 2.2 AA rules it can automate. Image-backed and gradient contrast, clipping inside scroll regions, and some native-control cases return `incomplete` and require human review. **A zero-violation result is not WCAG certification.**
- Registration exercises the capture UI and manual entry — not a physical ID, camera, or OCR integration. Backend OCR tests run separately.
- No production SMS/email, HIS, or pharmacy integration was exercised or is claimed.
- Signup and the privacy notice remain English; native-speaker review of Filipino and Cebuano is still outstanding.
