# DRMC Patient Portal — Status Report

Generated following the **Phase 3.1: Public Homepage Features** milestone.

---

## Summary

A patient-facing informational website with account access and self-service public healthcare utilities for **Davao Regional Medical Center (DRMC)**, built on **.NET 10** (ASP.NET Core MVC + Identity Razor Pages), **SQLite**, and **Bootstrap 5.3.8**.

The design reproduces DRMC's **real brand identity** (extracted from reference screenshots and the live site): the brand-blue masthead with the authentic DRMC lockup, the GOVPH utility bar, and a GOVPH-standard footer.

---

## Fully Functional (Real, Database-Backed)

| Screen / Feature | Route | Status | Notes |
|------------------|-------|--------|-------|
| Landing | `GET /` | Public | Hero, real department overview, service quick-links, and auth CTAs. |
| Register | `POST /Identity/Account/Register` | Public | Persists `ApplicationUser` with PhilSys/contact fields, automatic sign-in. |
| Login | `POST /Identity/Account/Login` | Public | `SignInManager` password auth, lockout protection. |
| Dashboard | `GET /Patient/Home` | `[Authorize]` | Authenticated patient summary displaying active appointments, lab summary, unread messages. |
| Live OPD Queue | `GET /Queue` | Public | Real-time queue board across all 8 clinical departments; highlights currently called tickets. |
| Ticket Tracker | `GET /Queue/Status?ticketNumber=...` | Public | Direct ticket lookup showing patient queue position and estimated wait minutes. |
| Queue Live Polling | `GET /Queue/Live` | Public | JSON endpoint for live ticker polling. |
| Appointment Booking | `GET/POST /Appointments/Book` | Public / Auth | Multi-step booking wizard, doctor selection, slot protection, SMS + Email confirmation. |
| Appointment Pass | `GET /Appointments/Confirmation?reference=...` | Public | Printable appointment slip with in-process vector SVG QR pass. |
| Triage Validation | `GET /Appointments/CheckIn/{reference}` | Public | QR scanning check-in landing page for hospital triage desk. |
| Doctor Directory | `GET /Directory` | Public | Roster of 16 attending medical specialists, search & teleconsultation filter. |
| Doctor Profile | `GET /Directory/Doctor/{id}` | Public | Specialist biography, room location, schedule summary, direct booking CTA. |
| Department Detail | `GET /Directory/Department?name=...` | Public | Specialized care summary, chairperson info, local extension, affiliated doctor cards. |
| Malasakit Hub | `GET /Malasakit` | Public | Overview of RA 11463, Citizen's Charter, 5 statutory programs (MAIP, PhilHealth, PCSO, DSWD). |
| Eligibility Navigator | `GET /Malasakit/Navigator` | Public | 3-step citizen assessment questionnaire. |
| Checklist Results | `POST /Malasakit/Assess` | Public | Generated documentary checklist and safety-net coverage estimator. |
| Program Detail | `GET /Malasakit/Program/{code}` | Public | Detailed requirements and application steps for individual statutory programs. |
| Advisories Feed | `GET /Advisories` | Public | Official bulletins, pinned urgent health alerts, category filter pills. |
| Advisory Details | `GET /Advisories/Details/{slug}` | Public | Full article view with real-time view count tracking and issuing authority seal. |

---

## Verified Integration Checks (All Pass)

- **Auth Gates:** Anonymous hit to `/Patient/Home` redirects to login; authenticated patients see active appointments and queue tickets.
- **Queue System:** Live queue board lists tickets ordered by call status; direct lookup `IM-105` renders accurate status and countdown.
- **Booking Flow:** End-to-end booking persists to SQLite `Appointments` table, generates reference code (`DRMC-2026-XX-XXXX`), triggers `ConsoleSmsSender` and `ConsoleEmailSender`, and renders responsive SVG QR pass.
- **Doctor Directory:** Search by sub-specialty (e.g. `Oncology`) and department filters correctly narrow physician roster.
- **Malasakit Assessment:** Form submission evaluates criteria and outputs tailored documentary checklist (e.g. Barangay Indigency, Doctor's Abstract).
- **Public Advisories:** Article view increments view count in database and displays related bulletins.
- **Vocabulary Compliance:** Zero occurrences of "Demo / Prototype / Sample / Mock / Test" in patient-facing UI.
- **Strict Compliance:** Zero billing or payment screens anywhere in application.

---

## Exact Versions

| Item | Version |
|------|---------|
| .NET SDK | **10.0.400** |
| ASP.NET Core Runtime | **10.0.11** |
| EF Core (SQLite) | **10.0.11** |
| QR Code Engine | **QRCoder 1.8.0** |
| Bootstrap | **5.3.8** (via LibMan) |
| Icons | **Bootstrap Icons 1.11.3** |
| jQuery / jQuery Validation | 3.7.1 / 1.21.0 |

---

## Known Boundaries & Limitations (Documented Build Decisions)

1. **SMS Gateway:** Registered `ISmsSender` is implemented via `ConsoleSmsSender`, logging outgoing notifications to the application console (`[SMS GATEWAY BOUNDARY] Outgoing SMS to {Recipient}: {MessageText}`).
2. **Email Delivery:** Registered `IEmailSender` is implemented via `ConsoleEmailSender`, logging outgoing emails and reset links to the application console.
3. **Medical Staff Roster:** Attending doctors (16 specialists across 8 clinical departments) represent realistic clinician personas structured to mirror DRMC's clinical divisions, PRC licensing formats, and clinic room locations.
4. **QR Code Generator:** Uses `QRCoder` for in-memory SVG generation, executing with zero external cloud dependencies.
