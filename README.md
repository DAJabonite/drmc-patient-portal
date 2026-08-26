# DRMC Patient Portal

A patient-facing informational website with account access for **Davao Regional Medical Center (DRMC)** — a Level III DOH Teaching and Training Hospital in Tagum City, Davao del Norte, Philippines (1,000-bed capacity; catchment area: Davao del Norte, Davao del Sur, Davao Oriental, Davao de Oro).

Real tagline: **"Caring for Life, Changing Lives."**

Built on **.NET 10** (ASP.NET Core MVC + Identity Razor Pages) with **SQLite** (zero external DB setup) and **Bootstrap 5.3.8**.

---

## What's here

### Converged Core Screens (Phase 1 & 2)
1. **Landing** (`/`, public) — hero with DRMC's real identity, interactive quick service links, a **Clinical Departments** section (8 departments, verified current against `drmc.doh.gov.ph/clinical-department/`), and calls-to-action into Register / Login.
2. **Register** (`/Identity/Account/Register`) — creates a patient account via ASP.NET Core Identity, persisting profile fields (Full Name, Contact Number) through an EF Core migration.
3. **Login** (`/Identity/Account/Login`) — standard email/password sign-in via `SignInManager`, with forgot-password (token flow).
4. **Home/Dashboard** (`/Patient/Home`, `[Authorize]`) — a calm, single-view summary with 4–6 clearly labeled cards backed by real seeded data, linking directly to live clinic queues, online booking, and doctor directory.

### Public Homepage Features (Phase 3.1)
5. **Live Outpatient Queue Tracker** (`/Queue`) — real-time waiting board with dynamic department filter tabs, currently served ticket display, estimated wait times, and direct ticket status lookup (`/Queue/Status?ticketNumber=...`) with AJAX polling endpoint (`/Queue/Live`).
6. **Online OPD Appointment Booking & QR Pass** (`/Appointments/Book`) — multi-step booking wizard with department, doctor, and timeslot selection; duplicate slot protection; database persistence; and generation of an instant digital QR triage check-in pass (`/Appointments/Confirmation`). Also includes triage validation check-in endpoint (`/Appointments/CheckIn/{reference}`).
7. **Find a Doctor & Specialty Clinic Directory** (`/Directory`) — comprehensive directory of 16 attending medical specialists across all 8 clinical departments, sub-specialty search, teleconsultation filters, detailed doctor profile views (`/Directory/Doctor/{id}`), and department overviews (`/Directory/Department?name=...`).
8. **Malasakit Center & Medical Social Services Navigator** (`/Malasakit`) — statutory overview under Republic Act No. 11463 (DOH MAIP, PhilHealth, PCSO IMAP, DSWD AICS), Citizen's Charter guidance, and an interactive 3-step questionnaire (`/Malasakit/Navigator`) generating personalized required document checklists and assistance estimates (`/Malasakit/Assess`).
9. **Public Health Advisories & Hospital Bulletins** (`/Advisories`) — public health advisory feed with category filters (Health Alerts, Immunization, Hospital Notices, Seasonal Health), pinned urgent health alert banners, view count tracking, and full article detail views (`/Advisories/Details/{slug}`).

### Design system
The design faithfully reproduces DRMC's real identity, extracted from reference screenshots and the live site (see `docs/design.md`): the brand-blue masthead with the actual DRMC lockup (DRMC seal + chrome wordmark + DOH seal), the GOVPH utility bar with Philippine Standard Time clock, two-level navigation, an accessible GOVPH-standard footer, and WCAG-2.2-AA-conscious contrast. Bootstrap 5.3 is themed via `--bs-*` CSS custom properties (no Sass recompile).

---

## How to run locally

Requires the **.NET 10 SDK**.

```bash
# from the repo root
source .dotnet-env.sh          # sets DOTNET_ROOT + PATH to the SDK in this sandbox
cd src/DrmcPatientPortal
dotnet run                     # listens on http://localhost:5000 (or a random port)
```

Open the URL printed in the console (e.g. `http://localhost:5000`).

- **Database:** SQLite, stored at `src/DrmcPatientPortal/app.db` (gitignored).
- **Connection string:** `DataSource=app.db;Cache=Shared` (in `appsettings.json` → `ConnectionStrings:DefaultConnection`).
- **Schema + seed:** handled automatically in Development by `DbInitializer` (`db.Database.Migrate()` + seed), so a fresh `dotnet run` just works. To manage migrations manually:
  ```bash
  dotnet ef migrations add <Name>
  dotnet ef database update
  ```

> Seeding only runs when the app's environment is `Development` (the default in `launchSettings.json`). In Production the database is expected to be provisioned separately — same code, no seed.

---

## Seeded accounts (reviewer logins)

These accounts are created automatically in Development. **Documented here only — never surfaced anywhere in the rendered UI.**

| Email | Password | Name | Role / Persona |
|-------|----------|------|----------------|
| `patient@drmc.doh.gov.ph` | `P@tient2026` | Maria Clara D. Santos | Primary reviewer account (has active appointments, queue ticket, lab results, messages) |
| `juan@drmc.doh.gov.ph` | `J@uan2026` | Juan Miguel A. Dela Cruz | Secondary reviewer account (bare account) |

Password policy is the default Identity policy (8+ chars, with upper/lower/digit/symbol) — the seeded passwords comply.

---

## Known limitations (intentional, for this build)

- **SMS Delivery (Boundary):** No external SMS aggregator/telecom gateway (e.g. Semaphore/Twilio) is connected. Outgoing SMS notifications (e.g. appointment confirmation texts) are logged to the **application log/console** via `ConsoleSmsSender` (`[SMS GATEWAY BOUNDARY] Outgoing SMS to {Recipient}: {MessageText}`). In a live production deployment, an HTTP/SMPP gateway implementation of `ISmsSender` is plugged in without changing domain logic.
- **Email Delivery (Boundary):** No external SMTP provider is configured. Identity email and appointment confirmations are written to the **application log/console** via `ConsoleEmailSender` in Development so the flow can be exercised end-to-end.
- **Doctor Roster (Boundary):** Attending physician profiles (16 specialists across 8 clinical departments) represent realistic clinician personas structured to mirror DRMC's tertiary services, sub-specialty clinics, PRC licensing numbers, and clinic room locations.
- **QR Code Generation:** Uses in-process vector SVG generation via `QRCoder` (`IQrCodeService`), requiring zero external network calls or cloud dependencies.

---

## Intentional scope (do not mistake "not built" for "broken")

- **Billing:** DRMC is a public government hospital operating under Universal Health Care and Medical Social Services safety nets; there is *no billing or payment UI anywhere* in this build.
- **Phase 3.2+ Authenticated Services:** Dedicated authenticated lab results detail modal viewer, trilingual language switcher, and audit logging engine are scheduled for subsequent Phase 3 iterations per `docs/PHASE3_PLAN.md`.

---

## Tech + packages

| Concern | Choice | Version |
|---------|--------|---------|
| Runtime | .NET (ASP.NET Core) | **10.0.400 SDK / 10.0.11 runtime** |
| Web | MVC + Razor Pages | net10.0 |
| Auth | ASP.NET Core Identity | 10.0.11 |
| ORM | EF Core (SQLite Provider) | 10.0.11 |
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
├── docs/
│   ├── design.md              # extracted design system tokens, typography, assets
│   ├── critique-rubric.md     # Gauntlet critique standards
│   ├── PHASE3_PLAN.md         # master blueprint for Phase 3 features & architecture
│   └── phase-logs/            # per-phase milestone summaries
└── src/
    └── DrmcPatientPortal/     # ASP.NET Core web app
        ├── Controllers/       # Home, Patient, Queue, Appointments, Directory, Malasakit, Advisories
        ├── Data/              # ApplicationDbContext, DbInitializer, Migrations
        ├── Models/            # ApplicationUser, Doctor, QueueTicket, Appointment, AssistanceProgram, PublicAdvisory
        ├── Services/          # IEmailSender, ISmsSender, IQrCodeService
        └── Views/             # Razor views for all 5 public features + core auth screens
```
