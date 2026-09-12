# Signup UI follow-up

Validation date: 2026-09-13. Follow-up to the desktop/mobile audit in PR #7.

## Changes

- The four-step indicator uses the same styles from first render onward. Equal-width columns align circles and labels. Completed steps show a check, the current step has `aria-current`, and manual entry marks the photo step as skipped. The circles are progress information rather than buttons that do nothing.
- The redundant “Step X of 4” badges are removed from every form step; the shared progress indicator and section headings provide the navigation context.
- All step action buttons share a 48px minimum height and regular text size. Desktop actions fit on one line; phone actions stack. The photo step keeps Back separate from Manual Entry and Scan ID.
- All registration JavaScript alerts are replaced by inline feedback. Required fields show their own messages and the first invalid field receives focus. Native form popups are disabled while client and server validation remain active. Enter advances the review step; final submission checks the entire form and prevents duplicate submissions.
- Scan failures no longer claim that autofill succeeded. The scan state disables conflicting controls and restores them after the response. Upload actions support keyboard activation.
- Server rejection reopens the appropriate step, preserves the return URL, and focuses the server error or invalid field. Existing successful signup and authenticator enrollment/sign-in flows still pass.
- Opening signup displays a privacy-and-terms dialog before form controls can be used. The agreement checkbox stays disabled until the scroll region reaches the bottom. The user must then explicitly check it and select **Agree and continue**. **Leave signup** returns to public home without acceptance. Keyboard Home/End, focus containment, and focus restoration are covered.
- The dialog and public privacy page share the existing privacy notice and new portal-use terms. The footer Terms of Use link points to the terms section. The credentials step allows the patient to review the notice again.

The existing server-enforced `PrivacyConsent` field remains the stored consent value; no database schema or consent-version audit record was added. Scrolling is a UI requirement, not proof that someone read or understood the text. No production dependencies were added.

## Verification

Run `pwsh scripts/test-ui.ps1 -SkipBrowserInstall` after the browser binaries are installed (omit the switch on first use).

- Release build: **0 warnings, 0 errors**.
- Existing backend tests: **25 passed**.
- Full browser suite: **42 passed**, including the previous 756 route visits across 18 profiles and 11 new signup test cases.
- Final focused signup run: **11 passed**; a subsequent nine-profile pass also refreshed the screenshots after stabilizing full-page capture in WebKit.
- Signup steps and notice: Chromium at 1440×900, 1920×1080, 768×1024, Pixel 5, and 320×568; WebKit iPhone 13, phone landscape, and desktop; Firefox desktop.
- Fresh mobile contexts use device screen dimensions, viewport, scale, mobile behavior, and touch. The focused checks use taps for agreement and Continue, and keyboard scrolling to verify the accessible path. Start-of-document and halfway scrolling keep the checkbox disabled; reaching the end enables it.
- Signup accessibility checks cover the dialog and four form steps at desktop and 320px widths: **10 axe scans**, in addition to the full suite's 84 screen scans, with zero reported violations. The existing limitations for automated/incomplete checks still apply.
- Tested inline required-field errors, password mismatch/show controls, manual entry, disabled scan, loading and simulated scan-service failure, duplicate-email server rejection, consent rejection on a direct POST, declining signup, successful account creation, and 2FA enrollment/sign-in.
- Visual review used the in-app browser for the dialog and inline errors, plus screenshots of every signup step on desktop and emulated phones. Desktop action heights and layout bounds are asserted rather than relying only on screenshots.

Full-run evidence is generated locally under `output/playwright/`: `signup-final-validation.log`, `test-results/User_ANGELO_2026-09-13_02_01_46_net10.0.trx` (backend) and `test-results/User_ANGELO_2026-09-13_02_01_52_net10.0.trx` (browser). The final focused run and screenshots are in `signup-focused-validation.log`, `signup-results/`, and `signup-*.png`.

## Screenshots

- [Desktop notice](screenshots/signup/desktop-notice.png) and [320px notice](screenshots/signup/mobile-notice.png).
- Desktop [ID selection](screenshots/signup/desktop-step1.png), [photo actions](screenshots/signup/desktop-step2.png), [inline errors](screenshots/signup/desktop-step3.png), and [credentials](screenshots/signup/desktop-step4.png).
- [iPhone inline errors](screenshots/signup/iphone-step3.png).

All entered values use isolated synthetic development data. Mobile results are emulation, not physical-device testing. Real camera/OCR capture and external notification integrations were not exercised in this UI follow-up. Signup and the existing privacy notice remain English; translation work is outside this follow-up.
