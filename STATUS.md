# DRMC Patient Portal — Status Report

Generated at the end of the Gauntlet Loop build.

---

## Summary

A patient-facing informational website with account access for **Davao Regional Medical Center (DRMC)**, built on **.NET 10** (ASP.NET Core MVC + Identity Razor Pages), **SQLite**, and **Bootstrap 5.3.8**. All four in-scope screens are built and connected end to end.

The design reproduces DRMC's **real brand identity** (extracted from reference screenshots and the live site): the brand-blue masthead with the authentic DRMC lockup, the GOVPH utility bar, and a GOVPH-standard footer.

---

## Fully functional (real, database-backed)

| Screen | Route | Status |
|--------|-------|--------|
| Landing | `GET /` | Public; hero, 8 real clinical departments, CTA → Register/Login. |
| Register | `POST /Identity/Account/Register` | Creates an `ApplicationUser`, persists **Full Name + Contact Number** via EF Core, signs the user in, redirects to dashboard. Verified end-to-end. |
| Login | `POST /Identity/Account/Login` | `SignInManager.PasswordSignInAsync`; success → dashboard; invalid → accessible "Invalid login attempt." Verified. |
| Home/Dashboard | `GET /Patient/Home` | `[Authorize]`; reads Next Appointment, Recent Lab Work, Messages, and Profile from the **database** (not hardcoded markup). Verified. |
| Manage profile | `GET /Identity/Account/Manage` | Identity manage-account page, restyled to brand blue. Verified reachable. |
| Forgot password | `GET/POST /Identity/Account/ForgotPassword` | Identity token flow; reset link output to console in Development. |

**Verified integration checks (all pass):**
- Anonymous hit to `/Patient/Home` redirects to the login page (auth gate works).
- Landing CTAs route to Register / Login.
- Login (seeded account) → dashboard; dashboard shows seeded appointment/labs/messages.
- Fresh registration persists and can be logged into in a new session.
- No patient-facing UI text contains "Demo / Prototype / Sample / Mock / Test" (the only "test" is the clinical phrase "your test is still being processed").

---

## Seeded / stubbed (display-only, by design)

These are **not broken** — they are intentionally display-only per the project scope:

- **Next Appointment** card — seeded rows read from DB; display-only (booking is out of scope).
- **Recent Lab Work** card — seeded rows; display-only (full results viewer out of scope).
- **Messages** card — seeded unread count; display-only (messaging out of scope).
- **Clinical Departments** card — real content from Landing; links to `/#departments`.
- Dashboard "empty states" (new accounts) correctly show "You have no upcoming appointments," "No recent lab results," "0 unread messages."

---

## Exact versions

| Item | Version |
|------|---------|
| .NET SDK | **10.0.400** |
| ASP.NET Core runtime | **10.0.11** |
| EF Core (Sqlite, Design, Tools, Identity, Identity.EntityFrameworkCore, UI, Diagnostics.EntityFrameworkCore) | **10.0.11** |
| Bootstrap | **5.3.8** (via LibMan) |
| jQuery / jQuery Validation | 3.7.1 / 1.21.0 |

---

## Out of scope (per spec — deliberately not built, including stubs)

Appointment booking · lab-results viewer · secure messaging · medical records · telehealth · bilingual toggle · family/proxy access · **billing** (public hospital — no billing anywhere). Dashboard cards for these are display-only, never broken links.

---

## How to run

```bash
source .dotnet-env.sh
cd src/DrmcPatientPortal
dotnet run
```
- SQLite file: `src/DrmcPatientPortal/app.db` (gitignored; regenerates via `Migrate()` in Development).
- Connection string: `DataSource=app.db;Cache=Shared` (`appsettings.json`).
- Schema and seed run automatically in Development (`DbInitializer`).

**Seeded reviewer logins** (README only, never shown in UI):
- `patient@drmc.doh.gov.ph` / `P@tient2026`
- `juan@drmc.doh.gov.ph` / `J@uan2026`

---

## Known limitation

Email delivery is not configured (no SMTP). The forgot-password reset link is written to the application log/console in Development via `ConsoleEmailSender`. Register a real `IEmailSender` for production.
