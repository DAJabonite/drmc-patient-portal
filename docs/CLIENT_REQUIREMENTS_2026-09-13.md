# Client requirements: September 13, 2026

## Official DRMC references

The live DRMC website was inspected in a browser on September 13, 2026. Its navigation links to:

- ARTA / Anti-Red Tape Act: https://drmc.doh.gov.ph/anti-red-tape-act/
- Citizen's Charter: https://drmc.doh.gov.ph/citizens-charter/

The portal now links directly to these maintained landing pages from the footer, OPD guide, and Malasakit pages. `OfficialDrmcSources` holds the URLs. No dated PDF, copied charter, embedded third-party scripts, or background scrape is served by the portal. Readers see the publisher's current content whenever they open a reference. If DRMC changes a landing-page URL without a redirect, the reference URL will need updating.

The local OPD and Malasakit preparation guides are not republished Citizen's Charter content and do not claim automatic synchronization. The Malasakit guide no longer presents its generated suggestions as eligibility approval or guaranteed coverage.

## Registration

The ID selector uses `PhilippineIdTypes` as its source. It retains existing common IDs and capture guidance, adds Senior Citizen and PWD IDs, and accepts all other government-issued IDs through an Other option. That option requires the ID name and government issuer; the supplied description is retained on the account and document record. Unknown card layouts support generic OCR and manual entry. Selecting manual entry now requires an explicit ID selection rather than assigning PhilSys automatically. Existing consent, image validation, encrypted storage, and account validation remain in place.

## Medical history

`GET /Patient/MedicalHistory` is authenticated, owner-scoped, audited, and not cached. It groups existing clinical encounters into OPD, ER, and Admitted; teleconsultations are grouped with OPD and retain their original encounter label. Category links filter the display, including empty states. Summaries link to the existing protected encounter detail route.

Access is through the dashboard, encounter page, desktop account menu, and mobile utility menu. The five main destinations remain Home, Encounters, Results, Medication, and Malasakit. No clinical records are fabricated to populate empty categories.

## Malasakit application and status

- `GET/POST /Malasakit/Apply`: authenticated application for medicines, diagnostics, or hospital care, with reported payment status, preparation checklist, and acknowledgement of review.
- `GET /Malasakit/Status`: own current application, eligibility, subsidy coverage, hospital-confirmed payment, separately labeled patient-reported payment, and review information when available.
- `POST /Malasakit/Requirements`: save the owner's document preparation checklist. Checked means prepared by the patient, not submitted or verified by staff.

Applications and checklists persist in SQLite. A unique patient index prevents duplicate current applications, including simultaneous submissions. Repeat Apply visits resume the current status page. Reads and writes audit access; mutation forms require antiforgery tokens. Form input cannot set patient ownership, eligibility, confirmed coverage, confirmed payment, or staff review notes.

New applications start Pending assessment, with coverage and hospital-confirmed payment unconfirmed. No billing, social-work, or eligibility adapter is available in this repository. The page explicitly tells patients to bring the reference and documents to the Malasakit Center; it does not claim that saving in the portal delivers the request to hospital staff. Institutional approval, confirmed billing updates, document submission/verification, and multiple application cycles require a subsequent approved staff workflow or integration.

The preparation checklist describes potentially relevant documents and directs patients to the social worker for current applicability and additional requirements. It is not an authoritative substitute for the live charter.

## Database and validation

Migration `20260913120223_AddSubsidyApplications` adds one table and an owner foreign key/unique index. Existing encounter, identity, and document schemas are unchanged. Run the normal EF migration step before deploying; Development applies migrations at startup.

Tests cover owner isolation, category mapping, required inputs and enum validation, duplicate applications, unconfirmed outcome defaults, checklist persistence, audit failures, government-ID variants, and existing database upgrade. Browser coverage includes registration, empty states, navigation count, filters, payment labels, reload persistence, and official reference URLs on desktop and phones. The new routes also participate in the existing responsive, localization, and automated accessibility matrix.

The adopted `docs/design.md`, existing DRMC tokens, Bootstrap components, and local icons are the visual source of truth. No additional runtime dependency or design system was introduced.

## Completed verification

- Release build completed without warnings or errors.
- 34 backend tests passed, including migration from the previous schema, preservation of an existing patient, and database-enforced duplicate prevention.
- 46 browser tests passed across Chromium, Firefox, and WebKit, including the new application and registration flows, the responsive matrix, and automated WCAG AA checks at desktop and 320px widths.
- All 228 resource keys match across English, Filipino, and Cebuano, with no duplicates.
- EF reports no model changes missing a migration; the diff whitespace check passes.
- Desktop and phone screenshots were visually inspected. Screenshots and TRX results are under ignored `output/playwright/`. These use synthetic test patients and an isolated database.

The broader suite exposed narrow search buttons in phone landscape and a transient contrast problem in outlined-button hover styling. Search controls now reserve enough width for their visible labels, and outline buttons change text/fill together. High-DPI long-page screenshot capture uses CSS pixels, with a viewport fallback above the browser image-height limit; full-document layout checks remain enabled.

The normal running development server was not restarted by this validation; tests launch a separate temporary server. No production deployment was performed.
