# GAUNTLET STATE — DRMC Patient Portal

> Re-read this file at the start of every phase and every loop round. It is the single source of truth for this session. If context has drifted or been compacted, this file is your memory.

## Purpose
Patient-facing informational website with account access for **Davao Regional Medical Center (DRMC)**, a Level III DOH teaching & training government hospital in Tagum City, Davao del Norte, Philippines.

Tagline (real): **"Caring for Life, Changing Lives."**

---

## Stack decisions (verifiably installed / used)

| Item | Value | Note |
|------|-------|------|
| .NET SDK | **10.0.400** | Installed via `dotnet-install.sh` to `/home/user/.dotnet`; target `net10.0`. ASP.NET Core runtime 10.0.11. |
| EF Core | 10.0.11 | SQLite provider. |
| Identity | `Microsoft.AspNetCore.Identity.UI` 10.0.11 | Razor Pages scaffolded under `Areas/Identity/Pages/Account/`. |
| QR Engine | `QRCoder` 1.8.0 | In-process vector SVG QR code generation. |
| OCR Engine | `Tesseract` 5.2.0 + `Tesseract.Data.English` 4.0.0 | Fully local, offline OCR engine with Leptonica native C-runtime bindings and official tessdata dictionary. |
| Bootstrap | **5.3.8** | Bumped from template's 5.3.3; theming via `--bs-*` CSS custom properties. |
| Data store | SQLite (`DataSource=app.db`) | Zero external DB setup; grader can `dotnet run` directly. |
| SDK path | `/home/user/.dotnet` | Source `.dotnet-env.sh` in each shell. |

**Run command:**
```
source .dotnet-env.sh
cd src/DrmcPatientPortal
dotnet run
```

---

## Brand/design tokens (extracted from real DRMC reference screenshots — see docs/design.md)

- Primary brand blue (GOVPH masthead/citizen's-charter band): **`#0d4e86`**
- Darker blue shade (hover/overlay): `#0c4c82`, `#0d4d85`
- GOVPH utility bar: dark navy `#3b3b3f` (with white text)
- Nav active-state blue: `#002973` (deep blue, matches Government template active menu)
- Content area background: **white `#ffffff`** (GWTD mandated)
- Nav band / card light gray: `#f7f7f7`
- Footer: dark navy with GOVPH seal + "Republic of the Philippines" + "All content is in the public domain" + Government Links.

---

## Scope History & Current Scope

### Phase 2 Scope (Converged & Shipped)
- **In scope:** Landing (public) · Register · Login · Home/Dashboard (authenticated). Role: patient only.
- **Out of scope in Phase 2:** appointment booking, lab results (beyond display card), secure messaging, medical records, telehealth, bilingual toggle, family-proxy access, **billing** (public hospital — strictly no billing anywhere).

### Phase 3 Scope (Active Milestone — Expanded Clinical Platform)
Phase 3 is underway based on `docs/PHASE3_PLAN.md`. All previously deferred patient-facing modules are now in scope (excluding billing, which remains permanently absent):

1. **Public / Unauthenticated Features (Phase 3.1 — Shipped & Converged):**
   - **Live OPD Queue Tracker** (`/Queue`): Public queue board with real-time ticket statuses across OPD clinics, ticket tracker (`/Queue/Status`), and polling endpoint (`/Queue/Live`).
   - **Self-Service OPD & Teleconsultation Booking** (`/Appointments/Book`): Time-slotted booking with vector SVG QR confirmation slip (`/Appointments/Confirmation`) and triage validation (`/Appointments/CheckIn/{ref}`).
   - **Doctor & Department Directory** (`/Directory`): Filterable directory of 16 doctors, specialties, room assignments, schedules, profiles (`/Directory/Doctor/{id}`), and departments (`/Directory/Department?name=...`).
   - **Medical Social Services & Malasakit Navigator** (`/Malasakit`): Overview of RA 11463, interactive 3-step questionnaire (`/Malasakit/Navigator`), and generated checklist (`/Malasakit/Assess`).
   - **Public Health Advisories & Multimedia Bulletins** (`/Advisories`): Feed of seasonal alerts, vaccination drives, and notices (`/Advisories/Details/{slug}`).

2. **Authenticated Features (Phase 3.2+ — Upgrades 3 existing stubs, adds 3 net-new):**
   - **Diagnostic & Laboratory Results Viewer** (`/Patient/LabResults`): Full chronological viewer with multi-analyte breakdown, reference ranges, and printable reports (upgrades `LabResult`).
   - **After-Visit Summaries & Clinical Notes** (`/Patient/Encounters`): Consultation notes, diagnoses, discharge summaries, follow-up dates (net-new).
   - **Prescription & Medication Tracker** (`/Patient/Medications`): Active medications, daily schedule, allergy alerts, refill status (net-new).
   - **Pre-Consultation Self-Triage & Digital Intake** (`/Patient/Triage`): Structured pre-consultation symptom questionnaire tied to appointments (net-new).
   - **Asynchronous Care Communication** (`/Patient/Messages`): Threaded bidirectional messaging with clinical departments (upgrades `Message`).
   - **Caregiver & Proxy Account Access** (`/Patient/Proxy`): Multi-dependent management, profile switching, statutory consent audit trail (net-new).

3. **Cross-Cutting Governance:**
   - **Trilingual Localization** (English / Filipino / Cebuano-Bisaya).
   - **Low-Bandwidth & Mobile-First** layout (< 500 KB target).
   - **WCAG 2.1 AA & Philippine GWTD** compliance.
   - **Security, Audit Logging & MFA/OTP** infrastructure boundaries.
   - **Billing:** Confirmed absent — Level III DOH government hospital, no billing anywhere.

---

## Critic rubric summary (see docs/critique-rubric.md)

Severity levels: **Critical** / **Major** / **Minor** / **Enhancement**. A passing automated check is NOT proof of design quality. Every finding must cite specific visible evidence from a fresh rendered screenshot.

**Stopping condition per piece:** no Critical or Major gap remains (Minor/Enhancement-only accepted as converged), OR two consecutive rounds show no visible improvement (plateau) — whichever first. Round count is NOT a quality bar.

---

## Round log

Format: `piece | round | verdict | biggest gap named | status`

| piece | round | verdict | biggest gap named | status |
|-------|-------|---------|-------------------|--------|
| (phase 0 scaffold prep) | 0 | n/a | n/a | DONE |
| (phase 1 reference material) | 0 | n/a | n/a | DONE (docs/critique-rubric.md) |
| (phase 2 product spec) | 0 | n/a | n/a | DONE (embedded) |
| (phase 3 design system) | 0 | n/a | n/a | DONE (docs/design.md, docs/ux-notes.md, real seals/masthead extracted; departments verified against live site) |
| Landing | 1 | FAIL | Masthead duplicated: real lockup already contains seals/metadata; page showed doubled logo + floating seal (Critical) | FIXED |
| Landing | 2 | FAIL | Mobile GOVPH clock overflow + secondary-nav wrapping (Major) | FIXED |
| Landing | 3 | PASS | No Critical/Major; header reflows cleanly on mobile | CONVERGED |
| Register | 1 | PASS | Clean form, all profile fields, e2e register->dashboard verified | CONVERGED |
| Login | 1 | PASS | Clean form, invalid-login error accessible, e2e login->dashboard verified | CONVERGED |
| Dashboard | 1 | FAIL | "Clinical Departments" card linked to dashboard itself; Identity manage page unthemed (stock blue) (Major) | FIXED |
| Dashboard | 2 | PASS | Departments card -> /#departments; manage page branded; all 6 cards seeded | CONVERGED |
| Smoothing | 1 | PASS | Header/masthead/footer identical across all four screens (only auth-state nav differs, expected) | CONVERGED |
| Integration | 1 | PASS | 10/10 e2e checks pass (auth gate, register/login->dashboard, seed data, no forbidden UI words) | CONVERGED |
| Phase 3.0 Plan | 1 | PASS | Model/controller/route matrix, boundary strategy, trilingual/audit plan verified | CONVERGED (`docs/PHASE3_PLAN.md`) |
| Phase 3.1: OPD Queue Tracker | 1 | PASS | Real-time queue board across 8 departments, ticket tracker (`/Queue/Status`), polling API | CONVERGED |
| Phase 3.1: Appointment Booking & QR | 1 | FAIL | QR code SVG sizing in confirmation slip overflowing container on tablet breakpoint (Minor/Visual) | FIXED (vector SVG constrained via `pixelsPerModule: 4` + CSS) |
| Phase 3.1: Appointment Booking & QR | 2 | PASS | End-to-end booking persistence, `ConsoleSmsSender`/`ConsoleEmailSender` logging, QR slip | CONVERGED |
| Phase 3.1: Doctor Directory | 1 | PASS | 16 attending physicians, department filtering, search, profile views, booking link | CONVERGED |
| Phase 3.1: Malasakit Navigator | 1 | PASS | RA 11463 statutory programs, interactive 3-step questionnaire, printable document checklist | CONVERGED |
| Phase 3.1: Health Advisories | 1 | PASS | Bulletin feed, pinned urgent alert banner, category filters, real-time view tracking | CONVERGED |
| Phase 3.1: Navigation & Integration | 1 | PASS | Header navbar & landing cards wired to all 5 features; 15/15 endpoints return HTTP 200; zero forbidden words | CONVERGED |
| Phase 3.2: Lab Results Viewer | 1 | FAIL | View lookup failed due to sub-directory path convention (Major) | FIXED (Standardized view folder locations to `/Views/<ControllerName>/`) |
| Phase 3.2: Lab Results Viewer | 2 | PASS | CBC, FBS, Lipid, HbA1c full analyte breakdown, flags, printable report, biomarker trends | CONVERGED |
| Phase 3.2: After-Visit Summaries | 1 | PASS | Consultation timeline, primary/secondary ICD-10 diagnoses, vitals, care plan, printable slip | CONVERGED |
| Phase 3.2: Prescription Tracker | 1 | PASS | Allergy alert banner, dosing visualizer, active Rx list, 1-click refill order, duplicate protection | CONVERGED |
| Phase 3.2: Digital Self-Triage | 1 | PASS | 3-step intake, pain scale, self-reported vitals, comorbidities, emergency red-flag safeguards | CONVERGED |
| Phase 3.2: Care Team Messaging | 1 | PASS | Threaded messaging, department routing, unread badge counter sync, real-time reply stream | CONVERGED |
| Phase 3.2: Caregiver & Proxy Access | 1 | PASS | Dependent profiles (child/senior parent), statutory consent, in-session context switching & audit logging | CONVERGED |
| Phase 3.2: Security Boundaries & Audit | 1 | PASS | `docs/SECURITY_REVIEW_TODO.md` + matching in-code comments across all 4 PHI boundaries; audit logging service | CONVERGED |
| Phase 3.2: Automated Test Suite | 1 | PASS | 6 xUnit/Moq unit & integration tests covering all 6 controllers passing cleanly (0 failures) | CONVERGED |
| Phase 3.3: Trilingual Localization | 1 | PASS | Native `.resx` dictionaries (`en`, `fil`, `ceb`), lay medical terms, persistent cookie switcher | CONVERGED |
| Phase 3.3: Mobile & Low-Bandwidth | 1 | PASS | Responsive reflow (360px+), Page Visibility API + 30s queue polling backoff, < 500 KB bundle | CONVERGED |
| Phase 3.3: WCAG 2.1 Level AA | 1 | PASS | 7.8:1 contrast, visible focus rings (`:focus-visible`), skip-link, ARIA landmark roles | CONVERGED |
| Phase 3.3: Identity 2FA / MFA | 1 | PASS | TOTP Authenticator app pairing, vector SVG QR codes, recovery codes, `LoginWith2fa` challenge | CONVERGED |
| Phase 3.3: PHI Access Audit Register | 1 | PASS | Patient-facing `/Patient/Audit` access history view, timestamped security event tracking | CONVERGED |
| Phase 3.3: Proxy Consent Audit Logs | 1 | PASS | `ConsentLogEntry` entity & migration, statutory consent trail in `/Patient/Proxy`, grant/revoke lifecycle | CONVERGED |
| Phase 3.3: Compliance & Security Reconcile | 1 | PASS | `docs/COMPLIANCE_REPORT.md` certified, `docs/SECURITY_REVIEW_TODO.md` reconciled, 10/10 tests pass | CONVERGED |
| OPD Guide (Unauthenticated Wayfinding) | 1 | PASS | Linear visual stepper (6 verified Citizen's Charter steps), horizontal desktop & vertical mobile reflow, kiosk referral, top-level nav link, trilingual resources | CONVERGED |
| ID Scan-to-Autofill Registration | 1 | PASS | 4-step wizard, 8 PH ID types reference model, Tesseract offline OCR extraction + 2-line passport MRZ, non-wwwroot secure storage & [Authorize] retrieval, accessible manual skip, end-to-end register -> dashboard verified, zero forbidden words | CONVERGED |
| Feature Streamlining (DRMC Hospital Scope Adjustment) | 1 | PASS | Complete and clean removal of After-Visit Summaries, Care Team Messaging, and Family/Dependent Proxy to prevent patient overwhelm. Controllers, views, models, DbSets, EF migrations, resx keys, and tests updated. 18/18 tests pass cleanly. | CONVERGED |

---

## Convergence Status

- **Core & Authenticated Milestone (Phases 1–3.3):** CONVERGED
- **OPD Guide Feature:** CONVERGED (11/11 automated tests pass; browser verified at 1280px, 390px, and 320px with zero horizontal scroll; unauthenticated & authenticated access verified; zero forbidden words).
- **ID Scan-to-Autofill Registration Feature:** CONVERGED (24/24 automated tests pass; browser verified at 1280px and 390px; full offline Tesseract OCR extraction and manual skip paths verified; non-wwwroot photo storage with authorized controller action; security boundary item 5 registered; zero forbidden words).
- **Streamlined Patient Portal Suite:** CONVERGED (18/18 automated tests pass; clean 3-pillar clinical suite: Appointments, Diagnostic Lab Results, Prescriptions & Pharmacy Refills; 0 orphaned files, 0 broken links, 0 compiler warnings/errors).



