# DRMC Patient Portal UX notes

These notes define how the portal reduces effort while preserving clinical, privacy, identity, and authorization requirements. Visual rules live in [design.md](design.md).

## Product principle

Patients should see the next useful action before institutional or explanatory content. “Fewer clicks” means one-tap access to frequent destinations and fewer repeated choices; it does not mean removing consent, validation, identity verification, emergency guidance, or access controls.

## Navigation

- Mobile and desktop primary navigation use the five client-requested destinations: Home, Encounters, Results, Medication, and Financial Assistance / Malasakit.
- Signed-in Home opens the patient dashboard; signed-out Home opens the public landing page.
- Guest access to Encounters, Results, and Medication follows the existing sign-in redirect and returns the patient to the destination after authentication.
- The icon-only mobile header hamburger opens Book Appointment, OPD Queue, directory, OPD guide, advisories, language, and account actions. It retains an accessible Menu label.
- Book and Queue remain one tap from the Home page and patient dashboard as prominent contextual actions.
- Current location is conveyed with text/icon styling and `aria-current`, not color alone.

## Public entry flow

1. Home presents Book Appointment, Track Queue, and Sign in/Dashboard first.
2. Secondary services—Malasakit, directory, advisories, and OPD guidance—remain directly available below or in Services.
3. Department previews link to the full directory instead of reproducing the entire directory on the home page.
4. Emergency contact information remains visible but does not compete with the main task hierarchy.

## Authentication and registration

- Login remains a single focused form and respects the requested return URL.
- Registration retains the four-step verified-ID flow: ID choice, image/manual path, review, and credentials/consent.
- Step labels stay concise on phones, Back/Continue actions remain full width, and focus moves to the active step heading.
- Server validation, upload handling, OCR, account lockout, privacy consent, and post-registration dashboard redirect remain unchanged.

## Dashboard and clinical records

- The dashboard opens with the patient's next appointment and its next valid action, followed by recent results and active medications.
- Access history and settings remain in the dashboard shortcut row and utility/account navigation; the welcome banner does not duplicate them.
- Record indexes use readable rows with a clear status and one primary action. Supporting metadata wraps instead of overflowing.
- Detail pages keep safety information and clinical wording intact while using consistent headings, definition-style metadata, and action placement.

## Booking and patient tools

- Booking remains one page so users can review the whole request before submission. Signed-in patient data continues to prefill.
- Route parameters continue to preselect department, doctor, or consultation type when supplied.
- Queue refresh, category filters, search, Malasakit questions, triage, refill requests, and print actions preserve existing behavior and accessible status announcements.

## Accessibility and older-phone handling

- Support 320px-wide layouts, 200% zoom, visible focus, semantic headings, and keyboard navigation.
- Interactive targets are at least 44px; bottom navigation includes safe-area padding.
- Horizontal scrolling is limited to clearly bounded filter rows; the document itself must not overflow.

## Security and operational boundaries

- Guest appointment capabilities remain exchanged into secure cookies and are never exposed in redesigned UI.
- Patient records retain owner checks and fail-closed audit behavior.
- The interface does not imply HIS, pharmacy, SMTP, SMS, or SIEM integration beyond configured production adapters.
- Messaging and caregiver/proxy access remain outside current scope and must not reappear as navigation links.
