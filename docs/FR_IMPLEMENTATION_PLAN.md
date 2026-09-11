# Functional Requirements Implementation Plan

**Target repository:** `DAJabonite/drmc-patient-portal`  
**Protected baseline:** `master` at `0a2ff2e847d31e93158a9e1e4401035bde6fa59c`  
**Integration branch:** `feature/functional-requirements-implementation`  
**Requirements baseline:** FR-01 through FR-133 and assumptions A-01 through A-26  
**Plan status:** Implementation started through small, reviewable phase branches.

## Approved scope amendment

`DEC-2026-09-11-MALASAKIT-001` overrides the restrictive interpretation of FR-73, FR-78, A-12, and A-15 for the Malasakit module. The existing Malasakit assessment must remain available, including program matching, documentary checklists, eligibility guidance, and estimated coverage calculation or promise behavior. This exception does not automatically approve personal financial-assistance status integrations outside Malasakit.

## Objective

Adapt the portal to the supplied requirements while preserving ASP.NET Core MVC, Razor, Bootstrap, EF Core/SQLite, the existing DRMC design language, and the approved Malasakit behavior. Work proceeds in focused pull requests; unapproved institutional behavior remains disabled through evidence gates.

## Branch strategy

- `master` is protected and remains the known baseline.
- `feature/functional-requirements-implementation` is the integration branch.
- Each phase is developed in a dedicated branch and merged into the integration branch only after review.
- Required checks: build, tests, migration validation, security checks, accessibility journeys, responsive checks, and requirement traceability.
- No approval may be inferred from seed data, placeholder integrations, or existing UI copy.

## Baseline assessment

### Reusable

- .NET 10 ASP.NET Core MVC, Razor, Bootstrap 5.3.8, Identity, TOTP 2FA, EF Core, SQLite, localization, anti-forgery, and ownership-filtered patient queries.
- Existing responsive UI, OPD guide, queue board, bulletin/advisory content, Malasakit, encounters, prescriptions, audit records, and ID OCR/manual-entry workflow.
- Existing English, Filipino, and Cebuano resources and xUnit/Moq test project.

### Conflicts to correct

| Existing behavior | Required direction |
|---|---|
| Registration requires email and signs users in immediately | Email optional; create Pending Verification accounts; allow only limited registration status until approval. |
| Password minimum is 8 | Provisional 12–128 characters with approved compromised-password screening. |
| One required privacy-consent checkbox | Separate notice acknowledgment from optional purpose-specific consent. |
| ID uploads lack complete type/size/signature/malware/retention controls | Add a safe, isolated upload and cleanup pipeline. |
| Detailed laboratory values, trends, reports, and downloads are exposed | Replace with approved metadata, status, and physical-release instructions only. |
| Appointment booking/cancellation/teleconsultation are active | Keep outside the FR baseline unless separately approved. |
| Medication refill actions are active | Make Prescriptions & Medications read-only. |
| Proxy/dependent accounts are active | Keep outside approved scope until separately approved. |
| Queue token lookup is predictable and unrestricted | Add private proof, throttling, freshness, and enumeration protection. |
| Session idle timeout is 30 minutes | Add provisional 15-minute inactivity, 8-hour absolute lifetime, warning, and invalidation controls. |
| Audit records are local and incomplete | Expand coverage and provide a tamper-resistant sink boundary. |

### Explicitly preserved

The Malasakit assessment, matching rules, documentary checklist, and estimated coverage output remain unchanged unless the repository owner issues a later decision.

## Target architecture

### Identity and verification

Add `PendingVerification`, `CorrectionRequired`, `Active`, `Rejected`, and `Suspended` lifecycle states; non-predictable registration references; separate contact-control verification; and narrowly scoped `ActivePatient`, `AccountVerifier`, `RecoveryOfficer`, `PrivacyOfficer`, and `ContentPublisher` policies.

### Source-state boundary

Patient and operational reads must distinguish `Loading`, `Available`, `ConfirmedEmpty`, `Stale`, `Unavailable`, `Offline`, and `Error`. Seed data never represents a Production source connection.

### Evidence gates

Approval records contain an enabled flag, evidence reference, accountable approver, approval timestamp, and boundary notes. Production gates fail closed when evidence is incomplete.

### Security and privacy

Apply record ownership authorization, no-store headers, safe logs, upload validation, throttling, idempotency, protected notification records, session controls, and category-based retention/disposal.

## Delivery phases

### Phase 0 — Governance and safety rails

**FR coverage:** FR-17, FR-60–69, FR-100, FR-114, FR-118–120, FR-133.

- Add feature approval/evidence registry with fail-closed Production behavior.
- Record the Malasakit scope amendment.
- Add standardized source states.
- Add no-store controls for authenticated and protected responses.
- Add CI, traceability, browser matrix, and governance documentation.

**Exit:** No unapproved feature can be enabled in Production; protected responses are non-cacheable.

### Phase 1 — Registration, verification, and recovery

**FR coverage:** FR-01–39.

- Make email optional and normalize mobile identifiers.
- Implement provisional mobile uniqueness behind the shared-phone decision boundary.
- Apply 12–128-character password policy and approved compromised-password adapter.
- Separate privacy acknowledgment from optional consent.
- Harden ID upload and retention disclosure.
- Create Pending Verification accounts with acknowledgment and limited protected status.
- Add correction-required, approval, rejection, and resubmission flows.
- Add least-privilege verification and recovery roles.
- Add generic responses, throttling, purpose-bound secrets, and session controls.

**Exit:** Pending accounts cannot retrieve patient records; Active accounts authenticate through approved identifiers.

### Phase 2 — Clinical release minimization and navigation

**FR coverage:** FR-40–71 and FR-88–92.

- Rebuild Home as a read-only summary with explicit source states.
- Release diagnoses and encounter details only through approved policy.
- Remove laboratory values, detailed findings, trends, print/download views, images, and previews from all patient-facing responses.
- Keep Encounters and Medical History read-only and non-duplicative.
- Make Prescriptions & Medications read-only.
- Apply `ActivePatient` and record-ownership policies plus no-store controls.
- Align all destination labels with FR-55.

**Exit:** Response inspection finds no prohibited laboratory findings; cross-patient identifiers reveal no content.

### Phase 3 — Public services, Malasakit, and queue safety

**FR coverage:** FR-56, FR-68–69, FR-72–87, and FR-93–99, as amended by `DEC-2026-09-11-MALASAKIT-001`.

- Add an Outpatient Department hub for Queue Tracker and OPD Guide.
- Align Advisories with the Bulletin Board information architecture.
- Add general Financial Assistance guidance while preserving the existing Malasakit assessment and estimated coverage behavior.
- Add approved Citizen’s Charter content and review metadata.
- Add public feedback/complaint submission, protected follow-up, and voluntary CSAT.
- Add restricted queue proof, manual refresh, source timestamp/time zone, stale state, and rate limiting.

**Exit:** Public pages expose no patient identifiers; queue and feedback references alone reveal no private record; Malasakit retains its approved calculator.

### Phase 4 — Digital ID and privacy rights

**FR coverage:** FR-66–67 and FR-74–76.

- Add an authenticated Digital ID with approved display name and opaque identifier.
- Limit QR payloads to an approved opaque reference.
- Require a separately approved verification workflow; QR possession alone discloses nothing.
- Add authenticated and identity-assisted privacy-rights requests with protected status and disposition.

**Exit:** QR decoding reveals no medical, contact, address, or full government-ID data.

### Phase 5 — Accessibility, responsive UX, localization, and performance

**FR coverage:** FR-54–55 and FR-101–123.

- Reconcile tokens to confirmed `#0E4E87` and label placeholders.
- Enforce persistent labels, field errors, keyboard support, focus management, reduced motion, and 44×44 primary touch targets.
- Test 320px reflow, 200% text, all named breakpoints, intermediate widths, orientations, keyboard, screen reader, contrast, and mobile keyboard states.
- Meet interaction budgets without bypassing safeguards.
- Provide approved English and Filipino; retain Cebuano as an approved additional locale only.
- Add privacy-reviewed performance measurement.

**Exit:** Manual WCAG 2.2 AA journey evidence and responsive results are recorded.

### Phase 6 — PWA and shared-device protection

**FR coverage:** FR-124–132.

- Add manifest, approved icons, optional install guidance, and browser fallback.
- Cache only the shell and explicit public allowlist.
- Exclude patient pages, responses, identifiers, medical information, and auth artifacts.
- Report offline submissions honestly and never silently queue sensitive forms.
- Clear app-managed sensitive state on logout/expiry.
- Defer disruptive service-worker updates during forms.

**Exit:** Browser storage contains no patient data; logout/back/restore and offline-submission tests pass.

### Phase 7 — Release hardening

- Rehearse migrations and rollback on sanitized data.
- Complete threat model, privacy review, retention schedule, key management, audit sink, and incident review.
- Enable approved notification providers without coupling delivery success to activation.
- Execute load, abuse, accessibility, security, and recovery tests.
- Produce final FR-01–FR-133 traceability and unresolved-evidence registers.

## Test strategy

- **Unit:** lifecycle transitions, evidence decisions, source states, minimization, QR payload, retention rules, and normalization.
- **Integration:** registration-to-activation, correction resubmission, status access, login/recovery, notification failure, ownership boundaries, and cache headers.
- **Security:** enumeration, CSRF, XSS, IDOR, path traversal, unsafe uploads, brute force, secret reuse, session fixation/revocation, and cache leakage.
- **Clinical release:** negative assertions that prohibited laboratory findings never reach patient-facing responses or browser-managed storage.
- **Accessibility:** keyboard/screen-reader journeys, reflow, text resize, contrast, reduced motion, focus restoration, and restrained announcements.
- **PWA:** cache allowlist, sensitive exclusion, logout clearing, safe update, and no background sensitive submission.

## Approval blockers

Production evidence is still required for identifiers/shared phones, upload limits and retention, staff roles, notification providers, clinical release policy, source systems and freshness, queue proof/rate limits, Citizen’s Charter and public content, Digital ID lifecycle, translations, brand assets, browser matrix, performance measurement, and PWA icons.

Malasakit calculation/coverage behavior is not in this blocker list because it has the explicit repository-owner decision `DEC-2026-09-11-MALASAKIT-001`.

## Pull-request sequence

1. `FR-000 governance-and-evidence-gates`
2. `FR-001 registration-lifecycle-and-verification`
3. `FR-002 authentication-recovery-and-session-security`
4. `FR-003 clinical-release-minimization`
5. `FR-004 public-services-and-queue-privacy`
6. `FR-005 feedback-charter-digital-id-privacy-rights`
7. `FR-006 accessibility-localization-performance`
8. `FR-007 pwa-cache-and-shared-device-security`
9. `FR-008 release-evidence-and-production-hardening`

Every PR updates traceability and tests for its mapped requirements.
