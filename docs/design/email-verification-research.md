# DRMC email verification UI proposal

Prepared October 6, 2026. Scope: the three supplied screenshots, plus resend feedback and invalid-link recovery. The accompanying HTML is an interactive design proposal; it sends no email and does not change the running application.

## Recommendation

Extend the existing DRMC authentication layout to every email verification page. Use one centered, left-aligned white card, the existing blue masthead and navigation, the existing government footer, and distinct pending, form, successful-request, confirmed and invalid-link states. Give the content enough vertical space that the footer follows the task instead of visually overtaking it.

The project already provides the right foundation in `wwwroot/css/site.css`: primary blue `#0e4e87`, dark blue `#093963`, canvas `#f5f7fa`, white surfaces, border `#d8e1ea`, muted text `#536273`, a 14px large radius and a restrained shadow. Login and registration already use `.auth-page`, `.auth-card` and DRMC buttons. Reuse these instead of introducing a new theme or dependency.

## What is causing the current appearance

- All three screenshot headings and messages start at the viewport edge, rather than inside a padded container.
- The resend form occupies only part of the page with an oversized heading and a brighter default Bootstrap primary button.
- The short confirmation bodies allow the large footer to occupy most of the first viewport.
- “Register confirmation” describes a technical page name; it does not tell the patient what to do.
- The confirmation success alert is dismissible even though it is the page’s main information, and it has no obvious next action.

Repository inspection found local custom login and register pages, but no local `RegisterConfirmation.cshtml`, `ResendEmailConfirmation.cshtml` or `ConfirmEmail.cshtml`. The project references `Microsoft.AspNetCore.Identity.UI`, so these appear to be fallback Identity pages. The shared layout renders `@RenderBody()` directly inside `<main>`, explaining why a fallback page without its own container can run edge to edge. This diagnosis is based on source inspection and the supplied screenshots, not a live trace of those routes.

## Research and what to adopt

| Reference | Relevant pattern | DRMC adaptation |
| --- | --- | --- |
| [NHS confirmation page](https://service-manual.nhs.uk/design-system/patterns/confirmation-page) | A prominent outcome, next steps, left-aligned content; avoid many competing components. NHS reports reassurance from its green completion panel. | A green icon and status label only after email confirmation, a plain outcome heading, and one sign-in action. This is an adaptation, not a claim that the proposed card has the NHS panel’s tested results. |
| [GOV.UK email confirmation](https://design-system.service.gov.uk/patterns/confirm-an-email-address/) | Explain the activation step, show where the email was sent, and provide resend and invalid-link recovery. Email access does not establish identity. | Show the registration email when the existing flow supplies it; explain the email link; give a resend route. Do not label this state “identity verified” or imply hospital records are linked. |
| [GOV.UK confirmation pages](https://design-system.service.gov.uk/patterns/confirmation-pages/) | Reassure users and explain what happens next. | Replace technical titles with patient instructions and an explicit next action. A PDF receipt or reference number adds no value to this small email step. |
| [USWDS alerts](https://designsystem.digital.gov/components/alert/) | Human-readable feedback, relevant recovery actions, status meaning conveyed by words as well as color, suitable ARIA roles. | Keep invalid-input feedback beside the field; use `role="status"` for asynchronous success feedback and `role="alert"` for errors. Main page success content remains visible. |

These public healthcare and government references fit the audience better than decorative marketing confirmation screens. Borrow their behavior and hierarchy while retaining DRMC branding; do not install their frameworks.

## Directions considered

| Direction | Fit and tradeoff |
| --- | --- |
| **Existing DRMC card layout — recommended** | Closest to current login and registration; compact, calm and straightforward to implement with Razor and Bootstrap. |
| Broad NHS-style success panel | Strong completed-task signal, but a large green panel would depart from the portal’s existing authentication cards. Consider it for larger completed transactions. |
| Two-column illustration and form | More visual impact, but uses valuable space and adds assets without helping a short confirmation task. Not recommended for these screens. |

## Screen specification

| Screen | Title and content | Primary action |
| --- | --- | --- |
| Registration confirmation | **Check your email**. Show the email supplied by the registration flow; explain opening the DRMC email and selecting its confirmation link. Include spam-folder help. Pending state uses blue, not completed-state green. | Send a new confirmation link → resend page. Secondary: Back to sign in. |
| Resend form | **Send a new confirmation link**. A visible “Email address” label above one input, with help and validation next to it. | Send confirmation link → submit the existing server form. Secondary: Back to sign in. |
| Resend feedback | **Check your inbox**. “If this email address belongs to an account that needs confirmation, we’ll send a new link.” Avoid disclosing whether an account exists or claiming confirmed delivery. | Back to sign in. Secondary text link to resend form. |
| Successful email confirmation | **Your email is confirmed**. “You can now sign in to the DRMC Patient Portal.” Green check and explicit confirmed label. Do not promise access to unlinked records or imply automatic sign-in. | Continue to sign in. |
| Invalid or expired confirmation | **This link is no longer valid**. Explain that a new link is needed, without falsely asserting the specific cause or expiry time. | Request a new link. Secondary: Back to sign in. |

Use a roughly 560px card, 36px desktop padding / 24px mobile padding, a 30px desktop heading / 26px mobile heading, 16px main text, 48px controls, and a 64px status icon. Preserve existing radii, system fonts and colors. Keep full official footer content; position it after the padded authentication region. The prototype uses a simplified static copy of the shell; production should reuse `_Layout.cshtml` and the existing mobile navigation.

## Implementation map

1. Add local Razor pages for `RegisterConfirmation`, `ResendEmailConfirmation` and `ConfirmEmail` under `Areas/Identity/Pages/Account`. Use page models for explicit confirmation state, generic resend feedback and local return destinations. Preserve POST handling, anti-forgery, binding and validation.
2. Add a small shared verification partial and narrowly scoped `.auth-verification-*` styles extending the existing auth card conventions. Keep it separate from clinical and admin pages.
3. Preserve the registration return URL through resend and successful confirmation when supported; validate local return destinations. Successful confirmation should direct to the existing sign-in page rather than imply the patient is already authenticated.
4. `Services/IdentityLinkPageFilter.cs` originally returned plain text for invalid links before `ConfirmEmail` rendered. The implementation moves email confirmation validation into the local page model: `UserManager.ConfirmEmailAsync` still verifies the token, and failure renders the branded recovery state with HTTP 400. Only `ConfirmEmail` is removed from that filter's registration. Reset-password and email-change retain their existing handling.
5. Keep resend responses generic for existing, missing and already-confirmed accounts. Only display recipient details supplied by an appropriate registration/session flow. Do not add a public account lookup to populate the preview’s sample address.
6. Do not add a resend countdown, “link expires in X minutes,” email delivery guarantee, email-provider shortcut or “change email” capability unless the server behavior actually supports it. Existing token behavior needs separate verification before promising that a newer link replaces an older one.
7. Localize new strings for English, Filipino and Cebuano through the project’s existing resource mechanism. Use the approved hospital support contact only if one is available; do not invent support details.

## Acceptance checks for implementation

Verify the normal registration → email → confirmation → sign-in journey and its return URL; valid resend and generic outcomes; malformed input; invalid and expired links; narrow mobile widths; keyboard navigation and visible focus; long email addresses; text wrapping in all supported languages; repeated requests; and absence of horizontal overflow. Confirm green success is shown only after successful server verification. Keep one h1 per state, decorative icons hidden from assistive technology, labels linked to inputs, and errors associated with the relevant field.

The HTML preview demonstrates five states, input validation and transitions. Its address is fictional. Shell controls and sign-in are illustrative; no authentication or delivery occurs. This proposal still needs review in the real app and with representative patients before claiming usability improvements.

Preview validation completed in Chrome: all five states showed no horizontal overflow at 320px and 390px mobile viewports; the pending state also fit at 1440px desktop width. Both logo images loaded. Each state had one h1. An empty resend form displayed the email validation message, and a valid sample address transitioned to generic request feedback. Desktop and mobile screenshots accompany the preview. These checks cover the isolated prototype, not production routes, localization or backend behavior.

## Approved implementation

The approved design is now implemented in the application's local Identity pages. It uses the existing portal shell, shared verification heading, scoped authentication styles and localized English, Filipino and Cebuano resources. Confirmation validates the real token before displaying success, keeps the patient signed out, and renders invalid or expired links with HTTP 400 and a resend action. Resend uses anti-forgery validation, a redirect after POST, generic feedback for missing and already-confirmed accounts, and a local return destination carried through the generated email link. Delivery exceptions are logged while retaining the same generic feedback. The unconfirmed-sign-in warning also carries the intended return destination into resend.

The static HTML and PNGs above remain design reference artifacts. Live-page validation and integration tests exercise the implementation separately; no delivery or authentication capability was added to the static preview.

Implementation validation: Release build with warnings treated as errors passed with zero warnings and errors; the full SQL Server suite passed all 74 tests, including 23 email verification cases; EF reported no pending model changes. Live Chrome checks covered the 1440px desktop pending page and 320px Cebuano resend and Filipino invalid-link pages, with no horizontal overflow, translated strings present and mobile card padding of 24px. Confirmation and resend behavior, expiry, HTTP statuses, anti-forgery and return destinations are covered by the integration cases.
