# DRMC Patient Portal UX notes

These notes define how the portal reduces effort while preserving clinical, privacy, identity, and authorization requirements. Visual rules live in [design.md](design.md).

## Product principle

Patients should see the next useful action before institutional or explanatory content. “Fewer clicks” means one-tap access to frequent destinations and fewer repeated choices; it does not mean removing consent, validation, identity verification, emergency guidance, or access controls.

## Navigation

- Mobile and desktop primary navigation use the five client-requested destinations: Home, Visits, Labs, Medication, and Financial Assistance / Malasakit.
- Signed-in Home opens the patient dashboard; signed-out Home opens the public landing page.
- Guest access to Visits, Labs, and Medication follows the existing sign-in redirect and returns the patient to the destination after authentication.
- The icon-only mobile header hamburger opens the directory, OPD guide, advisories, language, and account actions. It retains an accessible Menu label.
- Current location is conveyed with text/icon styling and `aria-current`, not color alone.

## Public entry flow

1. Home presents public hospital services, department previews, official references, and practical guidance before asking the patient to authenticate.
2. Malasakit, the directory, advisories, and OPD guidance remain directly available without an account.
3. Sign in and registration live in a dedicated portal-entry card near the bottom, so visitors can explore first and authenticate when ready.
4. Department previews link to the full directory instead of reproducing the entire directory on the home page.
5. Emergency contact information remains visible but does not compete with the main task hierarchy.

## Authentication and registration

- Login remains a single focused form and respects the requested return URL.
- Registration retains the four-step ID-entry flow: ID choice, image/manual path, review, and credentials/consent.
- Step labels and helper copy stay concise on phones, Back/Continue actions remain full width, and focus moves to the active step heading.
- The privacy-and-terms dialog keeps its scroll gate while using a clearly outlined agreement checkbox and readable notice text.
- Server validation, upload handling, OCR, account lockout, privacy consent, and post-registration dashboard redirect remain unchanged.

## Dashboard and clinical records

- The dashboard opens with direct links to doctors/clinics and the physical OPD guide, followed by current clinical-record summaries.
- Access history and settings remain in utility/account navigation; the welcome banner does not duplicate them.
- Record indexes use readable rows with a clear status and one primary action. Supporting metadata wraps instead of overflowing.
- Detail pages keep safety information and clinical wording intact while using consistent headings, definition-style metadata, and action placement.

## Patient tools

- Online appointment booking and self-service check-in are retired. Patients use the directory and physical OPD guide instead.
- Category filters, search, triage, and Malasakit application/checklist flows preserve accessible status announcements.
- Requirement pages favor concise headings and document lists; important document names are emphasized while copy counts remain regular weight.

## Accessibility and older-phone handling

- Support 320px-wide layouts, 200% zoom, visible focus, semantic headings, and keyboard navigation.
- Interactive targets are at least 44px; bottom navigation includes safe-area padding.
- Horizontal scrolling is limited to clearly bounded filter rows; the document itself must not overflow.

## Security and operational boundaries

- Retired booking and check-in endpoints remain unavailable and are not exposed in redesigned UI.
- Patient records retain owner checks and fail-closed audit behavior.
- The interface does not imply HIS, pharmacy, SMTP, SMS, or SIEM integration beyond configured production adapters.
- Messaging and caregiver/proxy access remain outside current scope and must not reappear as navigation links.
