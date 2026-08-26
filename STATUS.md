# DRMC Patient Portal — Status Report

Generated following the **Phase 3.2: Authenticated Dashboard Features** milestone.

---

## Summary

A patient-facing healthcare web application with self-service public utilities and a full authenticated clinical portal for **Davao Regional Medical Center (DRMC)**, built on **.NET 10** (ASP.NET Core MVC + Identity Razor Pages), **SQLite**, and **Bootstrap 5.3.8**.

The platform reproduces DRMC's **real brand identity** (extracted from reference screenshots and the live site): the brand-blue masthead with the authentic DRMC lockup, the GOVPH utility bar, a GOVPH-standard footer, and responsive clinical modules.

---

## Fully Functional (Real, Database-Backed)

### Public & Unauthenticated Features (Phase 3.1)
| Screen / Feature | Route | Status | Notes |
|------------------|-------|--------|-------|
| Landing | `GET /` | Public | Hero, real department overview, service quick-links, and auth CTAs. |
| Register | `POST /Identity/Account/Register` | Public | Persists `ApplicationUser` with PhilSys/contact fields, automatic sign-in. |
| Login | `POST /Identity/Account/Login` | Public | `SignInManager` password auth, lockout protection. |
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

### Authenticated Clinical Features (Phase 3.2)
| Screen / Feature | Route | Status | Notes |
|------------------|-------|--------|-------|
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
| Caregiver & Proxy Hub | `GET /Patient/Proxy` | `[Authorize]` | Dependent profile roster (minor child, senior parent, legal ward), active context indicator banner, statutory consent badges. |
| Add Dependent Profile | `GET/POST /Patient/Proxy/Add` | `[Authorize]` | Captures dependent demographics, PSA birth certificate / OSCA ID reference, and statutory consent agreement. |
| Profile Switching | `POST /Patient/Proxy/SwitchProfile/{id}` | `[Authorize]` | Switches active portal view context in Session, logs `PROXY_SWITCH` audit event. |
| Switch Back to Self | `POST /Patient/Proxy/SwitchToSelf` | `[Authorize]` | Reverts session context to guardian's personal health records. |

---

## Verified Integration & Automation Checks (All Pass)

- **Auth Gates:** Anonymous requests to any `/Patient/*` endpoint automatically challenge and redirect to login.
- **Automated Test Suite:** 6 xUnit + Moq unit/integration tests in `tests/DrmcPatientPortal.Tests` cover all 6 controllers and pass with 0 errors.
- **Browser Automation:** End-to-end user journeys executed by browser subagents across all 6 features (screenshots and WebP recordings archived in artifacts).
- **Session Profile Switching:** Switching to dependent "Joshua D. Santos" immediately updates the dashboard header with active proxy banner, and returning to personal records cleanses context seamlessly.
- **Refill Order Workflow:** Ordering Metformin refill decrements refill count from 2 to 1 and displays pick-up voucher for OPD Pharmacy Window 2; attempting immediate re-order displays pending alert banner.
- **Vocabulary Compliance:** Zero occurrences of "Demo / Prototype / Sample / Mock / Test" across all screens.
- **Strict Public Hospital Rule:** Zero billing or payment screens anywhere.

---

## Exact Versions

| Item | Version |
|------|---------|
| .NET SDK | **10.0.400** |
| ASP.NET Core Runtime | **10.0.11** |
| EF Core (SQLite) | **10.0.11** |
| QR Code Engine | **QRCoder 1.8.0** |
| Testing Framework | **xUnit 2.9.3 / Moq 4.20.72** |
| Bootstrap | **5.3.8** (via LibMan) |
| Icons | **Bootstrap Icons 1.11.3** |
| jQuery / jQuery Validation | 3.7.1 / 1.21.0 |

---

## Security Boundaries & Hardening Register

Tracked formally in [`docs/SECURITY_REVIEW_TODO.md`](file:///c:/Users/User/Documents/1Random%20Works/Drmc%20V2/drmc-patient-portal/docs/SECURITY_REVIEW_TODO.md) with corresponding in-code boundary markers:

1. **Asynchronous Care Communication:** Message payload encryption-at-rest (AES-256) and formal clinical RBAC required before production PHI ingestion.
2. **Caregiver & Proxy Access Trust Model:** HIMD institutional document verification (PSA birth certificates, guardianship decrees) and age-of-majority expiration workflows.
3. **PHI Access Audit Logging:** EF Core SQLite + `ILogger` audit trail is operational; needs remote WORM / SIEM streaming for tamper-proof compliance.
4. **Pre-Consultation Self-Triage Integration:** Patient intake is operational; clinician workstation UI and HL7 FHIR HIS transport planned for Phase 3.3.
