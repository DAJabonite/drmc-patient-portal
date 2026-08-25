# DRMC Patient Portal

A patient-facing informational website with account access for **Davao Regional Medical Center (DRMC)** — a Level III DOH Teaching and Training Hospital in Tagum City, Davao del Norte, Philippines (1,000-bed capacity; catchment area: Davao del Norte, Davao del Sur, Davao Oriental, Davao de Oro).

Real tagline: **"Caring for Life, Changing Lives."**

Built on **.NET 10** (ASP.NET Core MVC + Identity Razor Pages) with **SQLite** (zero external DB setup) and **Bootstrap 5.3.8**.

---

## What's here

Four patient-facing screens, all connected end to end:

1. **Landing** (`/`, public) — hero with DRMC's real identity, a **Clinical Departments** section (8 departments, verified current against `drmc.doh.gov.ph/clinical-department/`), and calls-to-action into Register / Login.
2. **Register** (`/Identity/Account/Register`) — creates a patient account via ASP.NET Core Identity, persisting profile fields (Full Name, Contact Number) through an EF Core migration.
3. **Login** (`/Identity/Account/Login`) — standard email/password sign-in via `SignInManager`, with forgot-password (token flow).
4. **Home/Dashboard** (`/Patient/Home`, `[Authorize]`) — a calm, single-view summary with 4–6 clearly labeled cards backed by real seeded data.

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

| Email | Password | Name |
|-------|----------|------|
| `patient@drmc.doh.gov.ph` | `P@tient2026` | Maria Clara D. Santos (has seeded appointments, lab results, messages) |
| `juan@drmc.doh.gov.ph` | `J@uan2026` | Juan Miguel A. Dela Cruz (bare account) |

Password policy is the default Identity policy (8+ chars, with upper/lower/digit/symbol) — the seeded passwords comply.

---

## Known limitation (intentional, for this build)

- **Email delivery is not wired.** No SMTP provider is configured. Identity email (e.g. the **forgot-password** reset link) is written to the **application log/console** (via `ConsoleEmailSender`) in Development so the flow can be exercised. In production you'd register a real `IEmailSender` (e.g. SendGrid/SMTP). This is a deliberate build decision, not a placeholder in the UI.

---

## Intentional scope (do not mistake "not built" for "broken")

Per the project spec, the following are **explicitly out of scope** and are *not* implemented, even partially:

- Appointment **booking** (the dashboard card is display-only and notes it's coming).
- Full **lab results** viewer (dashboard card shows a plain-language status, display-only).
- **Secure messaging** (dashboard card shows the unread count, display-only).
- **Medical records**, **telehealth**, **bilingual toggle**, **family/proxy access**.
- **Billing** — DRMC is a public government hospital; there is *no billing* anywhere in this build.

Any dashboard card for an out-of-scope module is intentionally **display-only**: no broken links, no fake "coming soon" pages. Either non-interactive (with an explanatory note) or linked to an already-built page (e.g. the "Clinical Departments" card links to the Landing's departments section `/#departments`).

---

## Tech + packages

| Concern | Choice | Version |
|---------|--------|---------|
| Runtime | .NET (ASP.NET Core) | **10.0.400 SDK / 10.0.11 runtime** |
| Web | MVC + Razor Pages | net10.0 |
| Auth | ASP.NET Core Identity | 10.0.11 |
| ORM | EF Core (SQLite) | 10.0.11 |
| Front-end | Bootstrap (via LibMan) | **5.3.8** |
| JS | jQuery, jQuery Validation | 3.7.1 / 1.21.0 |

---

## Repository layout

```
DrmcPatientPortal/
├── GAUNTLET_STATE.md          # build loop state + round log (this agent's working memory)
├── STATUS.md                  # current status report (what's functional vs. seeded)
├── docs/
│   ├── design.md              # extracted DRMC design tokens (colors, type, layout)
│   ├── critique-rubric.md     # adversarial critic rubric + WCAG baseline
│   └── ux-notes.md            # interaction/flow notes for the patient flow
├── src/DrmcPatientPortal/
│   ├── Controllers/           # HomeController (landing), PatientController (dashboard)
│   ├── Data/                  # ApplicationDbContext, DbInitializer, migrations
│   ├── Models/                # ApplicationUser, dashboard seeded models, ClinicalDepartments
│   ├── Services/              # ConsoleEmailSender
│   ├── Areas/Identity/Pages/Account/  # Register, Login (scaffolded + restyled)
│   ├── Views/                 # _Layout (GOVPH header/masthead/footer), Landing, Dashboard
│   └── wwwroot/               # site.css (design tokens), images (real DRMC seals/masthead)
```
