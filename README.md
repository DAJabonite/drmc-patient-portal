# DRMC Patient Portal

A comprehensive patient-facing web application and clinical portal for **Davao Regional Medical Center (DRMC)** — a Level III DOH Teaching and Training Hospital in Tagum City, Davao del Norte, Philippines (1,000-bed capacity; catchment area: Davao del Norte, Davao del Sur, Davao Oriental, Davao de Oro).

Real hospital tagline: **"Caring for Life, Changing Lives."**

Built on **.NET 10** (ASP.NET Core MVC + Identity Razor Pages) with **SQLite** (zero external DB setup), **Bootstrap 5.3.8**, and **xUnit / Moq**.

---

## What's here

### Converged Core Screens (Phases 1 & 2)
1. **Landing** (`/`, public) — hero with DRMC's real identity, interactive quick service links, an 8-department clinical overview, and calls-to-action into Register / Login.
2. **Register** (`/Identity/Account/Register`) — creates a patient account via ASP.NET Core Identity, persisting profile fields (Full Name, Contact Number, PhilSys/ID reference) through EF Core migrations.
3. **Login** (`/Identity/Account/Login`) — email/password authentication via `SignInManager` with lockout protection.
4. **Patient Dashboard** (`/Patient/Home`, `[Authorize]`) — unified command center with real-time counters for upcoming appointments, diagnostic laboratory results, active prescriptions, clinical visit notes, unread messages, and family proxy access.

### Public Homepage Features (Phase 3.1)
5. **Live Outpatient Queue Tracker** (`/Queue`) — real-time waiting board with dynamic department filter tabs, currently served ticket display, estimated wait times, and direct ticket lookup (`/Queue/Status?ticketNumber=...`) with AJAX polling endpoint (`/Queue/Live`).
6. **Online OPD Appointment Booking & QR Pass** (`/Appointments/Book`) — multi-step booking wizard with department, doctor, and timeslot selection; duplicate slot protection; database persistence; and generation of an instant digital QR triage check-in pass (`/Appointments/Confirmation`). Includes triage desk check-in endpoint (`/Appointments/CheckIn/{reference}`).
7. **Find a Doctor & Specialty Clinic Directory** (`/Directory`) — comprehensive directory of 16 attending medical specialists across all 8 clinical departments, sub-specialty search, teleconsultation filters, detailed doctor profile views (`/Directory/Doctor/{id}`), and department overviews (`/Directory/Department?name=...`).
8. **Malasakit Center & Medical Social Services Navigator** (`/Malasakit`) — statutory overview under Republic Act No. 11463 (DOH MAIP, PhilHealth, PCSO IMAP, DSWD AICS), Citizen's Charter guidance, and an interactive 3-step questionnaire (`/Malasakit/Navigator`) generating personalized required document checklists and assistance estimates (`/Malasakit/Assess`).
9. **Public Health Advisories & Hospital Bulletins** (`/Advisories`) — public health advisory feed with category filters (Health Alerts, Immunization, Hospital Notices, Seasonal Health), pinned urgent health alert banners, view count tracking, and full article detail views (`/Advisories/Details/{slug}`).

### Authenticated Clinical Platform Features (Phase 3.2)
10. **Diagnostic & Laboratory Results Viewer** (`/Patient/LabResults`) — chronological catalog with category filter tabs (Hematology, Clinical Chemistry, Special Diagnostics), accession search, multi-analyte breakdown table with reference ranges and color-coded diagnostic flags (`/Patient/LabResults/Details/{id}`), official printable report (`/Patient/LabResults/Print/{id}`), and biomarker trend tracker (`/Patient/LabResults/Trends`).
11. **After-Visit Summaries & Clinical Notes** (`/Patient/Encounters`) — chronological visit timeline, department filters, primary & secondary ICD-10 diagnoses, recorded vitals, physician progress notes, and patient home care plans (`/Patient/Encounters/Details/{id}`) with printable consultation slips (`/Patient/Encounters/Print/{id}`).
12. **Prescription & Medication Tracker** (`/Patient/Medications`) — active medication orders, prominent red allergy alert banner, 4-tier daily dosing visualizer (Morning / Noon / Evening / Bedtime), drug details (`/Patient/Medications/Details/{id}`), 1-click refill request submission with duplicate protection, and pharmacy pick-up vouchers (`/Patient/Medications/RefillStatus/{id}`).
13. **Pre-Consultation Self-Triage & Digital Intake** (`/Patient/Triage`) — 3-step symptom questionnaire linked to upcoming appointments (`/Patient/Triage/Start/{apptId}`), 0-10 visual pain scale, self-reported vitals (BP, temp, pulse, weight, blood sugar), comorbidity checklist, emergency red-flag safeguard intercept (`/Patient/Triage/EmergencyWarning`), and verified triage intake summary passes (`/Patient/Triage/Summary/{id}`).
14. **Asynchronous Care Communication** (`/Patient/Messages`) — threaded conversation inbox, category filter tabs, chronological message bubbles, automatic read receipt tracking that syncs dashboard unread badge counters (`/Patient/Messages/Thread/{id}`), and secure message composer (`/Patient/Messages/New`).
15. **Caregiver & Proxy Account Access** (`/Patient/Proxy`) — family & dependent hub, dependent registration with PSA birth certificate / OSCA ID references and statutory consent agreement (`/Patient/Proxy/Add`), in-session profile switching (`POST /Patient/Proxy/SwitchProfile/{id}`), active proxy banner indicators, and return to personal account (`POST /Patient/Proxy/SwitchToSelf`).

---

## Security Boundaries & Hardening Register

Tracked formally in [`docs/SECURITY_REVIEW_TODO.md`](file:///c:/Users/User/Documents/1Random%20Works/Drmc%20V2/drmc-patient-portal/docs/SECURITY_REVIEW_TODO.md) with corresponding in-code boundary markers:

1. **Asynchronous Care Communication:** Message payload encryption-at-rest (AES-256) and formal clinical RBAC required before production PHI ingestion.
2. **Caregiver & Proxy Access Trust Model:** HIMD institutional document verification (PSA birth certificates, guardianship decrees) and age-of-majority expiration workflows.
3. **PHI Access Audit Logging:** Structured EF Core SQLite + `ILogger` audit trail is operational; needs remote WORM / SIEM streaming for tamper-proof compliance.
4. **Pre-Consultation Self-Triage Integration:** Patient intake is operational; clinician workstation UI and HL7 FHIR HIS transport planned for Phase 3.3.

---

## How to run locally

Requires the **.NET 10 SDK**.

```bash
# from the repo root
source .dotnet-env.sh          # sets DOTNET_ROOT + PATH to the SDK in this sandbox
cd src/DrmcPatientPortal
dotnet run                     # listens on http://localhost:5095
```

Open the URL printed in the console (e.g. `http://localhost:5095`).

- **Database:** SQLite, stored at `src/DrmcPatientPortal/app.db` (gitignored).
- **Connection string:** `DataSource=app.db;Cache=Shared` (in `appsettings.json` → `ConnectionStrings:DefaultConnection`).
- **Schema + seed:** handled automatically in Development by `DbInitializer` (`db.Database.Migrate()` + seed), so a fresh `dotnet run` just works.

### Running Automated Tests

```bash
dotnet test
```

Executes the xUnit + Moq test suite in `tests/DrmcPatientPortal.Tests/` covering all 6 authenticated controllers.

---

## Seeded accounts (reviewer logins)

These accounts are created automatically in Development. **Documented here only — never surfaced anywhere in the rendered UI.**

| Email | Password | Name | Role / Persona |
|-------|----------|------|----------------|
| `patient@drmc.doh.gov.ph` | `P@tient2026` | Maria Clara D. Santos | Primary reviewer account (has active appointments, queue ticket, lab results, prescriptions, allergies, encounters, messages, and 2 dependents) |
| `juan@drmc.doh.gov.ph` | `J@uan2026` | Juan Miguel A. Dela Cruz | Secondary reviewer account (bare account) |

---

## Tech + packages

| Concern | Choice | Version |
|---------|--------|---------|
| Runtime | .NET (ASP.NET Core) | **10.0.400 SDK / 10.0.11 runtime** |
| Web | MVC + Razor Pages | net10.0 |
| Auth | ASP.NET Core Identity | 10.0.11 |
| ORM | EF Core (SQLite Provider) | 10.0.11 |
| Tests | xUnit / Moq | **2.9.3 / 4.20.72** |
| QR Generation | QRCoder | **1.8.0** |
| Front-end | Bootstrap (via LibMan) | **5.3.8** |
| Icons | Bootstrap Icons | **1.11.3** |
| JS | jQuery, jQuery Validation | 3.7.1 / 1.21.0 |

---

## Repository layout

```
DrmcPatientPortal/
├── GAUNTLET_STATE.md          # build loop state + round log (this agent's working memory)
├── STATUS.md                  # human-facing executive status report
├── README.md                  # project overview and run instructions
├── docs/
│   ├── design.md              # extracted design system tokens, typography, assets
│   ├── critique-rubric.md     # Gauntlet critique standards
│   ├── PHASE3_PLAN.md         # master blueprint for Phase 3 features & architecture
│   ├── SECURITY_REVIEW_TODO.md# formal register of security-review boundaries
│   └── phase-logs/            # per-phase milestone summaries
├── src/
│   └── DrmcPatientPortal/     # ASP.NET Core web app
│       ├── Controllers/       # Home, Patient, Queue, Appointments, Directory, Malasakit, Advisories, LabResults, Encounters, Medications, Triage, Messages, Proxy
│       ├── Data/              # ApplicationDbContext, DbInitializer, Migrations
│       ├── Models/            # Clinical models, enums, Identity models
│       ├── Services/          # IEmailSender, ISmsSender, IQrCodeService, IAuditLogService
│       └── Views/             # Razor views organized by feature controller
└── tests/
    └── DrmcPatientPortal.Tests/ # xUnit unit & integration test suite
```
