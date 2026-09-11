# Functional Requirements Implementation Plan

**Target repository:** `DAJabonite/drmc-patient-portal`  
**Protected baseline:** `master` at `0a2ff2e847d31e93158a9e1e4401035bde6fa59c`  
**Implementation branch:** `feature/functional-requirements-implementation`  
**Requirements baseline:** FR-01 through FR-133 and assumptions A-01 through A-26  
**Plan status:** Ready for staged implementation; approval-dependent behavior remains disabled.

## 1. Objective

Adapt the existing DRMC Patient Portal to the supplied Functional Requirements while preserving the established ASP.NET Core MVC, Razor, Bootstrap, SQLite/EF Core, DRMC visual language, responsive behavior, and accessible interaction patterns.

This is not a feature-addition-only release. The requirements explicitly remove or restrict several existing capabilities. Implementation must therefore combine:

1. controlled de-scoping of prohibited functionality;
2. new verification, privacy, administrative, feedback, Digital ID, and PWA capabilities;
3. stricter security and data-state handling; and
4. an evidence gate for DRMC-specific policy, legal, content, and integration decisions.

## 2. Non-negotiable branch strategy

- `master` remains the known baseline and must receive no direct implementation commits.
- All FR work is isolated in `feature/functional-requirements-implementation`.
- The FR branch starts from the exact current `master` commit.
- Delivery should occur through reviewed pull requests. Prefer small PRs by phase rather than one large merge.
- Required checks before merge: build, unit/integration tests, migration validation, security tests, accessibility journeys, responsive checks, and requirements traceability.
- Any unapproved institutional behavior must remain behind an explicit disabled configuration/evidence gate per FR-133.

## 3. Current-state assessment

### Reusable baseline

- .NET 10 ASP.NET Core MVC with Razor and Bootstrap 5.3.8.
- ASP.NET Core Identity, TOTP 2FA, EF Core, SQLite, localization, anti-forgery protection, and ownership-filtered patient queries.
- Existing responsive DRMC design, public navigation, OPD guide, queue board, advisories, Malasakit content, encounters, prescriptions, audit records, and ID OCR/manual-entry workflow.
- Existing English, Filipino, and Cebuano resource structure.
- Existing xUnit/Moq test projects and migrations.

### Major conflicts requiring correction

| Existing behavior | Required direction |
|---|---|
| Registration requires email and activates/signs in immediately | Email optional; create Pending Verification account; restrict patient records until authorized approval (FR-05, FR-10–15, FR-26–31). |
| Password minimum is 8 characters | Provisional 12–128-character policy, paste/password-manager support, compromised-password control (FR-07). |
| Privacy consent is one required checkbox | Separate notice acknowledgment from optional, purpose-specific consent (FR-08, FR-16–20). |
| ID uploads lack explicit size/type/signature/malware/retention enforcement | Add safe upload pipeline, disclosure, isolation, cleanup, and evidence-gated retention (FR-18–19, FR-62–65). |
| Portal exposes lab values, analytes, trends, print views, and report details | Remove detailed laboratory findings from every patient-facing page/response/cache; show metadata/status/release instructions only (FR-42, FR-46–53). |
| Appointment booking, cancellation, teleconsult links are enabled | FR baseline is read-only and A-24 excludes booking/rescheduling/cancellation/teleconsultation unless separately approved. |
| Medication refill submission is enabled | Prescriptions & Medications becomes read-only; no renewal/refill generation (FR-90–92, A-24). |
| Proxy/dependent access is enabled | A-24 excludes proxy/dependent accounts from approved scope; disable and preserve code only outside active routes until re-approved. |
| Queue lookup accepts a predictable token alone and estimates wait time | Require approved restricted proof, throttling, freshness states, and no guaranteed wait estimate (FR-81–87). |
| Malasakit navigator calculates an estimated coverage outcome | Convert to approved informational guidance; do not imply live eligibility or guaranteed coverage (FR-72–73, FR-78). |
| Session idle timeout is 30 minutes; no absolute limit or warning | Provisional 15-minute inactivity, 8-hour absolute limit, two-minute warning, session invalidation controls (FR-34–39). |
| Local audit table is mutable and not comprehensive | Expand event coverage and add tamper-resistant/remote sink boundary (FR-64–65). |
| No staff verification workflow, privacy request flow, Digital ID, feedback/CSAT, OPD hub, or PWA foundation | Add narrowly scoped modules with least-privilege roles and evidence gates (FR-12–25, FR-60, FR-66–78, FR-93–99, FR-124–133). |

## 4. Target architecture

### Identity and verification

Add account lifecycle states: `PendingVerification`, `CorrectionRequired`, `Active`, `Rejected`, `Suspended`. Store non-predictable registration references and verification decisions separately from Identity credentials. Introduce narrowly scoped policies such as `ActivePatient`, `AccountVerifier`, `RecoveryOfficer`, `PrivacyOfficer`, and `ContentPublisher`.

### Data-source boundary

Every patient/operational read must return an explicit state: `Loading`, `Available`, `ConfirmedEmpty`, `Stale`, `Unavailable`, `Offline`, or `Error`. Development seed data is never evidence of a production source. Source adapters must expose freshness and approval metadata.

### Evidence gate

Introduce an `ApprovedFeatureRegistry` or equivalent configuration-backed service containing approval status, evidence reference, effective date, owner, and environment restrictions. Disabled features must fail closed and must not publish placeholder claims.

### Security and privacy

Use ownership authorization at every record query, no-store headers for authenticated responses, masked logs, safe upload validation, server-side throttling, idempotency for submissions, protected notification records, secure session lifecycle, and category-based retention/disposal jobs.

## 5. Delivery phases

### Phase 0 — Governance, traceability, and safety rails

**Requirements:** FR-17, FR-60–69, FR-100, FR-114, FR-118–120, FR-133; A-01–A-26.

- Add a machine-readable requirements traceability register mapping every FR to implementation, test, evidence dependency, and status.
- Add approval/evidence gates and production-safe defaults.
- Add CI checks for build, test, migration drift, dependency audit, secret scanning, formatting, and traceability completeness.
- Establish protected-response middleware and standardized source-state/view components.
- Record exact supported browser matrix and dependency versions.

**Exit criteria:** Every FR has an owner and test strategy; no unapproved feature can be enabled in Production.

### Phase 1 — Registration, verification, and recovery

**Requirements:** FR-01–39.

- Make email optional and normalize mobile identifiers.
- Add provisional mobile uniqueness handling with a documented shared-phone decision boundary.
- Change password policy to 12–128 characters and add compromised-password screening through an approved privacy-preserving adapter.
- Split privacy notice acknowledgment and optional-purpose consent records.
- Harden ID upload: extension-independent file signature checks, allowlisted image types, configurable size limits, image decoding, metadata stripping, malware adapter boundary, isolated temporary storage, expiry cleanup, and retention disclosure.
- Create Pending Verification accounts without protected-record access.
- Add registration acknowledgment and limited protected status page.
- Add correction-required/resubmission/rejection workflows without duplicate accounts.
- Add least-privilege verifier UI and decision audit trail.
- Add optional mobile/email contact-control verification distinct from identity verification.
- Add mobile-first recovery and identity-assisted lost-number recovery.
- Add recent reauthentication for password changes and revoke other sessions.
- Add rate limiting, generic outward responses, purpose-bound single-use secrets, idle/absolute session controls, and expiry warning.

**Primary schema additions:** account status, registration reference, verification case/decision, privacy acknowledgment, consent record, contact-verification transaction, notification delivery attempt, recovery transaction, session/security stamp metadata.

**Exit criteria:** Pending users can view only registration status; Active users can authenticate by approved mobile/email identifier; enumeration and duplicate-submission tests pass.

### Phase 2 — Clinical release minimization and protected navigation

**Requirements:** FR-40–71, FR-88–92.

- Rebuild Home as a read-only summary with explicit source states.
- Add latest diagnosis only through an approved release-policy service.
- Replace lab details, values, trends, print/download routes, and analyte payloads with consultation/direct-request metadata, `Available`/`In Progress`, stale/unavailable states, and physical-release instructions.
- Remove sensitive lab data from HTML, JSON, logs, caches, source maps, and client storage.
- Keep Encounters reverse chronological and limited; connect to a separate read-only Medical History view without duplicating full records.
- Make Prescriptions & Medications read-only and remove refill-generation actions.
- Apply `ActivePatient` policy and no-store cache controls to all protected pages.
- Align navigation labels exactly with FR-55 and maintain current-page indication across desktop/mobile.

**Exit criteria:** Automated response inspection finds no detailed lab findings anywhere; direct-ID authorization tests pass for every patient record type.

### Phase 3 — Public service information and queue safety

**Requirements:** FR-56, FR-68–69, FR-72–87, FR-93–99.

- Add a public Outpatient Department hub linking to Queue Tracker and OPD Guide.
- Convert advisories into the approved Bulletin Board information architecture while preserving list/detail behavior.
- Separate Financial Assistance guidance from Malasakit program guidance; remove eligibility/coverage calculation claims.
- Add Citizen’s Charter content model with approval, source, review date, effective date, responsible office, fees, channels, and commitment fields.
- Add public feedback/complaint form, non-predictable acknowledgment reference, protected follow-up, optional anonymous-contact behavior, and voluntary CSAT.
- Redesign queue lookup around an approved private lookup secret or authenticated access; keep secrets out of URLs/logs/analytics.
- Add manual refresh, source timestamp/time zone, stale threshold, unavailable state, and rate limiting.

**Exit criteria:** Public pages expose no patient identifiers; observed queue or feedback references alone reveal no private record.

### Phase 4 — Digital ID and privacy-rights workflows

**Requirements:** FR-66–67, FR-74–76.

- Add authenticated Digital ID page with approved display name and opaque portal identifier.
- Generate a minimized QR payload containing only an approved opaque reference.
- Do not provide public patient-record resolution from QR possession.
- Add authenticated privacy access/correction/erasure-or-blocking requests plus identity-assisted route.
- Add protected request status and authorized disposition; never auto-delete medical records.

**Exit criteria:** QR decoding reveals no medical/contact/government-ID data; privacy decisions are role-restricted and auditable.

### Phase 5 — Accessibility, responsive UX, localization, and performance

**Requirements:** FR-54–55, FR-101–123.

- Reconcile design tokens to confirmed `#0E4E87`; label unapproved tokens as placeholders.
- Preserve approved logo aspect ratio and alternatives.
- Enforce persistent labels, input purposes, keyboard hints, field-level errors, focus management, live-region restraint, reduced motion, and 44×44 primary targets.
- Test at all named breakpoints and intermediate widths, 320px reflow, 200% text, portrait/landscape, keyboard-only, screen reader, contrast, and mobile keyboard states.
- Meet activation-count budgets without bypassing safeguards.
- Provide approved English and Filipino content; keep Cebuano only as an explicitly approved additional locale.
- Add performance instrumentation subject to privacy approval and distinguish lab from field results.

**Exit criteria:** Manual WCAG 2.2 AA journey evidence exists; core journeys pass responsive and interaction-budget tests.

### Phase 6 — PWA foundation and shared-device protection

**Requirements:** FR-124–132.

- Add manifest, approved icons, and optional install guidance.
- Cache only the shell and explicit allowlisted public guidance.
- Deny service-worker caching for authenticated pages, APIs, identifiers, medical data, and auth artifacts.
- Add offline/online truth states; never queue sensitive submissions automatically.
- Clear app-managed sensitive state on logout/expiry and prevent back/restore from displaying protected content.
- Defer disruptive service-worker activation during forms and offer explicit safe update.

**Exit criteria:** Cache Storage/IndexedDB inspection contains no patient data; logout/back/restore and offline-submission tests pass.

### Phase 7 — Release hardening and production evidence

- Run migration rehearsal against a sanitized copy and verify rollback.
- Complete threat model, privacy review, retention schedule, key-management design, and incident/logging review.
- Replace console SMS/email with approved providers only after evidence is recorded; delivery failure never rolls back activation.
- Execute load, abuse, accessibility, security, and disaster-recovery tests.
- Produce final FR-01–FR-133 traceability report and unresolved-approval register.

## 6. Proposed repository changes

```text
src/DrmcPatientPortal/
├── Authorization/          # policies and ownership handlers
├── Features/
│   ├── RegistrationStatus/
│   ├── StaffVerification/
│   ├── PrivacyRequests/
│   ├── DigitalId/
│   ├── Feedback/
│   └── CitizenCharter/
├── Infrastructure/
│   ├── EvidenceGates/
│   ├── Notifications/
│   ├── RateLimiting/
│   ├── Retention/
│   └── SourceStates/
├── Middleware/             # no-store, session expiry, security headers
└── wwwroot/
    ├── manifest.webmanifest
    └── service-worker.js

tests/
├── DrmcPatientPortal.Tests/
├── DrmcPatientPortal.IntegrationTests/
├── DrmcPatientPortal.SecurityTests/
└── DrmcPatientPortal.AccessibilityTests/

docs/
├── FUNCTIONAL_REQUIREMENTS.md
├── FR_IMPLEMENTATION_PLAN.md
├── REQUIREMENTS_TRACEABILITY.csv
├── APPROVAL_AND_EVIDENCE_REGISTER.md
├── THREAT_MODEL.md
└── RELEASE_EVIDENCE/
```

## 7. Test strategy

- **Unit:** lifecycle transitions, data minimization, source-state mapping, QR payload, retention rules, normalization, policy decisions.
- **Integration:** registration through activation, correction resubmission, protected status, login/recovery, notification failure, authorization boundaries, no-store headers.
- **Security:** enumeration, CSRF, XSS, IDOR, path traversal, upload polyglots, oversized files, brute force, secret reuse, session fixation/revocation, cache leakage.
- **Clinical-release:** negative assertions that detailed laboratory findings never appear in patient-facing responses or browser-managed storage.
- **Accessibility:** keyboard and screen-reader journeys, 320px reflow, 200% text, contrast, reduced motion, focus restoration, dynamic announcements.
- **PWA/offline:** cache allowlist, sensitive exclusion, logout clearing, safe update, no background sensitive submission.
- **Performance:** approved low-end mobile laboratory profile plus privacy-reviewed field metrics.

## 8. Approval blockers

Implementation may proceed behind disabled boundaries, but Production must not enable affected behavior until DRMC supplies approval/evidence for:

- accepted identifiers, shared-phone handling, and email-login ownership;
- ID types, upload limits, malware controls, retention/disposal, and OCR approval;
- verifier/recovery/privacy roles and responsible offices;
- SMS/email providers, retry policy, wording, and service commitments;
- clinical release rules for diagnoses, encounters, medications, and all laboratory metadata;
- source systems, patient linkage, freshness thresholds, and outage semantics;
- queue proof mechanism and rate limits;
- Citizen’s Charter, Malasakit, financial-assistance, bulletin, feedback, anti-fixer, and contact content;
- Digital ID identifier, QR lifecycle, and verification workflow;
- English/Filipino translations, Cebuano scope, brand assets/tokens, browser matrix, performance measurement, and PWA icons.

## 9. Recommended PR sequence

1. `FR-000 governance-and-evidence-gates`
2. `FR-001 registration-lifecycle-and-verification`
3. `FR-002 authentication-recovery-and-session-security`
4. `FR-003 clinical-release-minimization`
5. `FR-004 public-services-and-queue-privacy`
6. `FR-005 feedback-charter-digital-id-privacy-rights`
7. `FR-006 accessibility-localization-performance`
8. `FR-007 pwa-cache-and-shared-device-security`
9. `FR-008 release-evidence-and-production-hardening`

Each PR must update the traceability register and include tests for every mapped requirement. No phase should silently infer approval from development seed data, existing UI copy, or placeholder integrations.
