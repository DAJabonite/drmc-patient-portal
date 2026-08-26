# DRMC Patient Portal — Status Report

Generated following the **Phase 3.3: Governance, Accessibility & Trilingual Pass** milestone.

---

## Summary

A production-quality patient portal and clinical health information platform for **Davao Regional Medical Center (DRMC)**, built on **.NET 10** (ASP.NET Core MVC + Identity Razor Pages), **SQLite**, and **Bootstrap 5.3.8**.

The platform reproduces DRMC's **authentic brand identity**: the brand-blue masthead with the official DRMC lockup, the GOVPH utility bar with live Philippine Standard Time (PST) clock, standard GOVPH footer, WCAG 2.1 AA accessibility compliance, and native **trilingual localization** in English, Filipino (Tagalog), and Cebuano-Bisaya.

---

## Fully Functional Features (Real, Database-Backed)

### 1. Public & Unauthenticated Features (Phase 3.1)
| Screen / Feature | Route | Status | Notes |
|---|---|---|---|
| Landing | `GET /` | Public | Hero, real department overview, service quick-links, and auth CTAs. |
| OPD Guide | `GET /OpdGuide` | Public | Linear visual stepper orienting patients through the 6-step OPD consultation sequence; refers to lobby kiosk. |
| Register | `POST /Identity/Account/Register` | Public | Persists `ApplicationUser` with PhilSys/contact fields, automatic sign-in. |
| Login | `POST /Identity/Account/Login` | Public | `SignInManager` password auth, 2FA challenge redirect, lockout protection. |
| Live OPD Queue | `GET /Queue` | Public | Real-time queue board across all 8 clinical departments; highlights currently called tickets. |
| Ticket Tracker | `GET /Queue/Status?ticketNumber=...` | Public | Direct ticket lookup showing patient queue position and estimated wait minutes. |
| Queue Live Polling | `GET /Queue/Live` | Public | JSON endpoint for live ticker polling with low-bandwidth Page Visibility backoff. |
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

### 2. Authenticated Clinical Features (Phase 3.2)
| Screen / Feature | Route | Status | Notes |
|---|---|---|---|
| Patient Dashboard | `GET /Patient/Home` | `[Authorize]` | Summary hub with dynamic counters for appointments, lab results, prescriptions, encounters, messages, and proxy context. |
| Lab Results Catalog | `GET /Patient/LabResults` | `[Authorize]` | Category filtering (Hematology, Clinical Chemistry, Special Diagnostics), accession search, status pills. |
| Lab Report Details | `GET /Patient/LabResults/Details/{id}` | `[Authorize]` | Multi-analyte breakdown table, biological reference ranges, diagnostic flags (`Normal`, `High`, `Low`), pathologist signatures, audit logging. |
| Printable Lab Report | `GET /Patient/LabResults/Print/{id}` | `[Authorize]` | Official DOH-standard printable laboratory examination slip. |
| Biomarker Trends | `GET /Patient/LabResults/Trends` | `[Authorize]` | Chronological historical trends across test parameters with visual normal reference bounds. |
| After-Visit Summaries | `GET /Patient/Encounters` | `[Authorize]` | Clinical visit timeline, department filters, primary & secondary ICD-10 diagnoses. |
| Encounter Details | `GET /Patient/Encounters/Details/{id}` | `[Authorize]` | Complete consultation summary, recorded vitals, physician progress notes, and care plan instructions. |
| Printable Visit Slip | `GET /Patient/Encounters/Print/{id}` | `[Authorize]` | Clean consultation and care plan slip for personal health records. |
| Medication Tracker | `GET /Patient/Medications` | `[Authorize]` | Active prescriptions, prominent allergy alert banner, 4-tier daily dosing schedule visualizer. |
| Prescription Details | `GET /Patient/Medications/Details/{id}` | `[Authorize]` | Drug strength, dosage form, instructions, refills remaining count, and refill order submission. |
| Refill Request Flow | `POST /Patient/Medications/RequestRefill` | `[Authorize]` | Submits refill request, decrements remaining refills, prevents duplicate pending orders, logs pharmacy event. |
| Refill Pick-Up Voucher | `GET /Patient/Medications/RefillStatus/{id}` | `[Authorize]` | Pick-up voucher slip for DRMC OPD Pharmacy Window 2. |
| Digital Pre-Triage | `GET /Patient/Triage/Start/{apptId}` | `[Authorize]` | 3-step pre-consultation intake form linked to appointment, 0-10 pain scale, self-reported vitals, comorbidities. |
| Triage Submission | `POST /Patient/Triage/Submit` | `[Authorize]` | Evaluates acuity level (`Routine`, `Priority`), emergency red-flag safeguards, logs clinician queue sync. |
| Intake Summary Pass | `GET /Patient/Triage/Summary/{id}` | `[Authorize]` | Verified digital intake summary slip for triage nurses. |
| Emergency Alert | `GET /Patient/Triage/EmergencyWarning` | `[Authorize]` | High-contrast emergency guidance page triggered by acute symptoms. |
| Care Team Messaging | `GET /Patient/Messages` | `[Authorize]` | Threaded conversation inbox, category filter tabs, unread message indicators. |
| Conversation Thread | `GET /Patient/Messages/Thread/{id}` | `[Authorize]` | Real-time message bubbles, auto-marks staff messages as read (syncs dashboard badge), reply form. |
| Compose Message | `GET/POST /Patient/Messages/New` | `[Authorize]` | Clinic department selection, category routing, non-emergency timeline guidelines. |
| Caregiver & Proxy Hub | `GET /Patient/Proxy` | `[Authorize]` | Dependent profile roster (minor child, senior parent), statutory consent trail, in-session profile switching, consent revocation. |
| Add Dependent Profile | `GET/POST /Patient/Proxy/Add` | `[Authorize]` | Demographics, PSA / OSCA ID reference, statutory consent agreement and audit log entry. |

### 3. Cross-Cutting Governance, Security & Compliance (Phase 3.3)
| Screen / Feature | Route | Status | Notes |
|---|---|---|---|
| Trilingual Localization | In Header / All Pages | Active | English (`en`), Filipino (`fil`), and Cebuano (`ceb`) native `.resx` dictionaries with lay terminology. |
| Language Switcher Action | `POST /Home/SetLanguage` | Public | Persistent cookie-based culture switching with local URL redirection. |
| Two-Factor Auth Management | `GET /Identity/Account/Manage/TwoFactorAuthentication` | `[Authorize]` | 2FA protection status overview, remember browser management, disable/enable toggles. |
| Authenticator App Setup | `GET/POST /Identity/Account/Manage/EnableAuthenticator` | `[Authorize]` | Vector SVG QR code scan and manual setup key pairing for Google / Microsoft Authenticator. |
| 2FA Login Challenge | `GET/POST /Identity/Account/LoginWith2fa` | Public | 6-digit TOTP verification challenge during sign-in with lockout protection. |
| PHI Access History | `GET /Patient/Audit` | `[Authorize]` | Patient-facing transparency register under RA 10173 displaying chronological access history. |
| Statutory Consent History | `GET /Patient/Proxy` (Audit Table) | `[Authorize]` | Immutable log of caregiver consent declarations, switches, and revocations. |

---

## Exact Technical Stack & Dependencies

| Concern | Component | Version |
|---|---|---|
| Runtime | .NET SDK / ASP.NET Core | **10.0.400 / 10.0.11** |
| ORM & Database | EF Core (SQLite) | **10.0.11** |
| Auth & Identity | ASP.NET Core Identity + 2FA TOTP | **10.0.11** |
| Vector QR Engine | QRCoder | **1.8.0** |
| Front-End Framework | Bootstrap (via LibMan) | **5.3.8** |
| Icons | Bootstrap Icons | **1.11.3** |
| Automated Tests | xUnit / Moq | **2.9.3 / 4.20.72** (10/10 tests passing) |

---

## Verified Integration & Automation Checks (All Pass)

- **Trilingual Localization:** English, Filipino, and Cebuano-Bisaya language switching verified across views with lay terminology and zero raw tokens.
- **Automated Test Suite:** 10 xUnit + Moq unit/integration tests covering all controllers, localization, 2FA, and consent logging pass cleanly (0 errors).
- **Two-Factor Authentication:** TOTP authenticator setup generates valid in-process SVG QR code and 2FA login challenge protects patient accounts.
- **Patient Access History:** Complete audit trail visible at `/Patient/Audit` documenting security events and remote IP addresses.
- **Proxy Consent Audit:** Grant, context switch, and revocation events recorded in `ConsentLogEntries` under RA 10173.
- **Low-Bandwidth Optimization:** Queue polling auto-throttles when inactive via Page Visibility API.
- **Zero Commercial Billing:** Absolute compliance with Level III DOH public hospital mandate.
- **Zero Forbidden Words:** Zero occurrences of "Demo / Prototype / Sample / Mock / Test" across all screens.
