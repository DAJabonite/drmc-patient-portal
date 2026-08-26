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

---

## Phase 3.1 Convergence Note

All 5 public homepage features and navigation integrations are fully implemented, DB-backed with EF Core SQLite, styled using the authentic DRMC design system tokens, and verified end-to-end with zero errors and zero warnings. Final milestone status: **CONVERGED**.
