---
status: adopted
updated: 2026-09-12
owners: DRMC patient portal team
---

# DRMC Patient Portal design system

## Product and audience

- **Purpose:** Give patients fast, trustworthy access to appointments, queue information, clinical records, medication information, and hospital services.
- **Primary users:** Patients and family members, often using older phones, slower connections, or assistive technology.
- **Primary tasks:** Book a consultation, track the OPD queue, review results and medications, and find the next required action.
- **Density:** Calm and concise. Show the next useful action first, then disclose supporting detail.

## Design direction

- **Character:** Reassuring, direct, civic.
- **Signature idea:** A restrained DRMC-blue frame around clear, white task surfaces.
- **Should feel like:** A dependable public hospital service that is easy to use with one hand.
- **Must not feel like:** A copied government portal, a marketing landing page, or a generic card-heavy SaaS dashboard.

## Sources and provenance

| Source | Role | Adapt | Do not copy |
| --- | --- | --- | --- |
| Client brief, 2026-09-12 | Product direction | `#0E4E87`, mobile-first, fewer clicks, less visual noise | No requirement to preserve the old website layout |
| Client navigation clarification, 2026-09-12 | Information architecture | Home, Encounters, Results, Medication, and Financial Assistance / Malasakit as the five permanent primary destinations | Do not replace a requested destination with a generic “More” tab |
| `wwwroot/images/drmc-masthead.png` and DRMC seals | Identity | Official marks and institutional name | Do not redraw or decorate the marks |
| https://drmc.doh.gov.ph/ | Brand reference | DRMC blue and public-service character | Page composition, dense navigation, and dated visual patterns |
| https://www.nhs.uk/ | Responsive health-service reference | Clear service taxonomy, task-first health links, and separate account/search utilities | NHS branding, page composition, or content |
| https://www.healthdirect.gov.au/ | Responsive health-service reference | Compact mobile identity, a clear utility menu, and direct task language | Healthdirect branding, palette, or page composition |
| Bootstrap 5 and Bootstrap Icons | Interaction system | Existing grid, controls, offcanvas, utilities, and local icons | No additional UI framework |

The live DRMC site returned HTTP 403 during the 2026-09-12 implementation review. The checked-in identity assets and the client's explicit color direction are therefore the implementation source of truth.

## Foundations

### Color

- Primary/action: `#0E4E87`; hover/pressed: `#093963`; deep emphasis: `#062846`.
- Canvas: `#F5F7FA`; surface: `#FFFFFF`; soft surface: `#EEF4F8`; border: `#D8E1EA`.
- Text: `#17212B`; secondary text: `#536273`.
- Gold `#F2B32A` is reserved for one high-priority call to action or important attention, never general decoration.
- Success, warning, and danger always include text or an icon and never rely on color alone.

### Typography

- Use the existing system sans-serif stack. Do not load remote fonts.
- Page titles use a compact responsive scale; body copy remains at least 16px on phones.
- Keep paragraphs near 65 characters per line and use plain, health-literate labels.

### Spacing and geometry

- Use a 4px base with common steps of 8, 12, 16, 24, 32, and 48px.
- Touch targets are at least 44px. Mobile gutters are 16px; desktop content remains within the existing Bootstrap container.
- Controls and cards use 10–14px radii. Full pills are reserved for statuses, filters, and compact segmented actions.
- Use borders and spacing before shadows. Standard surfaces use no shadow or a single low-elevation shadow.

### Iconography, imagery, and motion

- Use locally vendored Bootstrap Icons and the existing DRMC assets only.
- Icons support labels; they do not replace unfamiliar action text.
- Keep the facade image to the public landing hero. Clinical and account screens use plain surfaces.
- Motion communicates state in 120–200ms and is removed under `prefers-reduced-motion`.

## Layout and responsive behavior

- Mobile is the baseline. Content must reflow without page-level horizontal overflow at 320px.
- On phones, use a compact 64–72px brand header and a fixed five-item bottom navigation. Utility navigation opens in one accessible offcanvas sheet from the header.
- The five primary destinations are always Home, Encounters, Results, Medication, and Malasakit. “Financial Assistance / Malasakit” is shortened to “Malasakit” only where mobile width requires it, with the full accessible label retained.
- Signed-in Home opens the patient dashboard; signed-out Home opens the public landing page. Protected record destinations retain the existing sign-in redirect and return URL.
- Booking, queue tracking, directory, OPD guidance, advisories, language, and account actions remain available in the mobile utility menu and as contextual page actions.
- Desktop uses the same five-destination information architecture, plus the existing account controls.
- The public home page is task-first: Book, Queue, and Sign in/Dashboard lead; secondary hospital information follows.
- List and detail pages use compact page headers, flat record rows, and horizontally scrollable filter groups where necessary.
- Print views hide navigation and interactive chrome and retain their current clinical content.

## Components and states

- **Page header:** eyebrow only when useful, one H1, optional short description, and at most one primary action.
- **Buttons:** solid primary for the page's main action, outline or text for secondary actions. Avoid multiple equal-weight buttons.
- **Cards/rows:** one surface per information group; avoid cards nested inside cards.
- **Statuses:** compact label plus readable text; include pending, success, warning, error, empty, and disabled states.
- **Forms:** visible labels, persistent validation, correct input modes, full-width mobile actions, and no removal of required consent or verification steps.
- **Mobile navigation:** fixed to the safe-area-aware viewport edge, exposes current location with `aria-current`, and never makes page actions or content unreachable. The hamburger is icon-only with a localized accessible name and a minimum 44px touch target. Focused controls scroll clear of both fixed bars.
- **Record metadata:** badges wrap; references occupy a separate line on phones. Appointment status moves below the heading on phones. The dashboard uses three columns only at the extra-large breakpoint.
- **Desktop dropdowns:** account menus overlay the page without changing navigation height.

## Accessibility and content

- Target WCAG 2.2 AA contrast, visible focus, semantic landmarks, logical headings, and keyboard-operable menus and forms.
- Maintain a 44px minimum interactive area and support 200% zoom and reduced motion.
- Keep English, Filipino, and Cebuano resource keys in parity. Clinical content remains verbatim and is not machine-translated.
- Use direct labels such as “Book”, “Results”, and “Medications”; reserve explanatory copy for decisions or safety information.

## Implementation mapping

| Design concept | Existing implementation | Rule |
| --- | --- | --- |
| Brand and semantic tokens | `wwwroot/css/site.css` | Extend the existing CSS variables; do not add a parallel theme |
| Shared shell and navigation | `Views/Shared/_Layout.cshtml` | Keep routes and auth checks; vary priority by sign-in state and viewport |
| Mobile task navigation | `Views/Shared/_MobileNavigation.cshtml` | Use Bootstrap offcanvas and existing localization/resources |
| Patient workflows | Razor views under `Views` and `Areas/Identity/Pages` | Preserve handlers, field names, validation, and security boundaries |

## Anti-patterns and non-goals

- No giant page heroes after the landing page.
- No repeated calls to action, decorative badges, pill-shaped primary buttons, glass effects, or nested card stacks.
- No hiding functionality to simplify a screen.
- No new routes, dependencies, API contracts, persistence changes, or invented patient data as part of visual work.

## Decisions

- Adopted: `#0E4E87` remains the primary brand color.
- Adopted: the client-requested five primary destinations remain stable for guests and signed-in patients; authentication changes the Home destination and access boundary, not the navigation vocabulary.
- Adopted: booking and queue are high-priority tasks surfaced on Home/Dashboard and in the header utility menu rather than replacing requested primary destinations.
- Adopted: necessary registration, privacy, triage, confirmation, and clinical safety steps remain intact.
- Adopted: the portal is inspired by DRMC identity but does not reproduce the older public website's layout.
