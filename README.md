# DRMC Patient Portal

Patient-facing web application for Davao Regional Medical Center, built with ASP.NET Core 10 MVC and SQLite. It provides hospital information, patient registration, and access to locally stored patient records.

This is a working development/review portal, **not an integrated production hospital backend**. Development initializes example patients, doctors, records, and advisories. Clinical data is not fetched from DRMC; hospital, pharmacy, billing/social-work, and SIEM integrations are absent. Seeded names, schedules, notices, and guidance require institutional verification before use with patients.

## Current features

- Public home page, doctor/department directory with search, OPD guide, health advisories, privacy/terms, and official DRMC reference links.
- Malasakit hub with four service guides: DSWD, DRMC Malasakit, DOH-MAIFIP, and PhilHealth. Requirement sections expand independently.
- Registration with government-ID selection, optional local OCR/photo capture, manual entry, privacy consent, and required residential address.
- Identity sign-in, lockout, profile management, authenticator-based two-factor authentication, and an owner-only ID wallet with encrypted images when supplied.
- Dashboard, **Visits** with OPD/emergency/inpatient and date filters, linked care notes and laboratory reports, laboratory category/search/date filters, and medications with exact dose schedules and allergy information.
- Patient access history; record-detail access is audited. English, Filipino, and Cebuano shared resources cover navigation and selected flows; some forms and clinical content remain English.

The following endpoints are retained and tested but are **not linked from the current primary navigation**: medical history (`/Patient/MedicalHistory`), local subsidy application/checklist/status (`/Malasakit/Apply`, `/Malasakit/Status`), program details (`/Malasakit/Program?code=MAIP`), and appointment-linked triage (`/Patient/Triage/Start/{appointmentId}`). Triage requires an existing owned appointment; the portal cannot book one. Subsidy outcomes remain pending/unconfirmed until an institutional review supplies them.

Online booking/check-in, queue tracking, refill submission/dispensing, messaging, proxy access, and laboratory trend charts are not active features. Retired appointment endpoints return 404. No staff workstation is included.

## Technology stack

- .NET 10, ASP.NET Core MVC/Razor and Identity
- Entity Framework Core 10 with SQLite and versioned migrations
- ASP.NET Core Data Protection for ID images and temporary upload tokens
- Tesseract 5.2 with bundled English data for OCR; QRCoder for authenticator enrollment
- Local Bootstrap 5, Bootstrap Icons, jQuery and unobtrusive validation; no Node build required
- xUnit/Moq/SQLite tests; Playwright Chromium, Firefox and WebKit browser tests with axe accessibility checks

## Project structure

```text
src/DrmcPatientPortal/
  Program.cs                 Service registration, middleware, Development initialization
  Controllers/               MVC endpoints (VisitsController matches website terminology)
  Views/                     Home, Visits, LabResults, Medications, Malasakit, etc.
  Areas/Identity/Pages/       Registration, sign-in, profile, ID wallet, 2FA
  Models/                    Persisted entities, view inputs, static hospital catalogs
  Data/                      ApplicationDbContext, DbInitializer, migration history
  Services/                  Audit, encrypted documents, OCR, QR, notifications
  Resources/                 English, Filipino and Cebuano shared strings
  wwwroot/                   Local CSS, JavaScript, images and vendor libraries
tests/                      Backend and browser test projects
scripts/test-ui.ps1          Release build, browser installation and complete test suite
docs/                       Deployment, design, content provenance and historical validation
run.ps1                     Local development launcher
```

The Visits view directory, controller, and generated URLs use the website name. `ClinicalEncounter`, `ClinicalEncounters`, and existing audit/resource keys retain their database/domain names for compatibility. `/Patient/Encounters` and its detail URLs remain supported aliases. Laboratory and medication directory names already match their full page names.

## Prerequisites and installation

Install the .NET 10 SDK. PowerShell is needed for the supplied helper scripts. The browser suite downloads its browser engines on first setup. OCR tests generate images using System.Drawing and require Windows; native OCR dependencies must also be validated on the deployment platform.

From the repository root:

```powershell
dotnet restore DrmcPatientPortal.slnx
dotnet build DrmcPatientPortal.slnx -c Release
dotnet run --project src/DrmcPatientPortal --launch-profile http
```

Open `http://localhost:5095`. Alternatively run `./run.ps1`. The HTTP launch profile sets `Development`; first startup applies the complete migration chain and seeds example data. For local HTTPS, trust a development certificate with `dotnet dev-certs https --trust` and select the `https` launch profile (`https://localhost:7104`).

Development accounts, for local review only:

| Account | Password | Data |
|---|---|---|
| `patient@drmc.doh.gov.ph` | `P@tient2026` | Visits, laboratory reports, prescriptions, allergies, historical triage fixtures |
| `juan@drmc.doh.gov.ph` | `J@uan2026` | Empty clinical record, useful for empty-state checks |

Never expose these accounts in a production installation. Startup does not reset existing passwords or patient-entered data. To review a fresh dataset, point Development at a new disposable database instead of deleting an existing database.

## Configuration

Defaults are in `src/DrmcPatientPortal/appsettings.json`. Environment variables use double underscores for nested keys; use a secret store or local user secrets for credentials.

| Setting | Purpose/default |
|---|---|
| `ConnectionStrings__DefaultConnection` | SQLite connection, `DataSource=app.db;Cache=Shared`; use an absolute path for predictable deployment storage |
| `DataProtection__KeyRingPath` | Persistent encryption keys; default `App_Data/DataProtectionKeys` |
| `PatientDocuments__RootPath` | Encrypted ID files; default `App_Data/PatientIdDocuments` |
| `PatientDocuments__TemporaryPath` | Staged registration uploads; default `App_Data/TempUploads` |
| `PatientDocuments__MaximumFileSizeMb` | Upload limit, 10 MB |
| `PatientDocuments__TemporaryFileLifetimeMinutes` | Temporary upload lifetime, 30 minutes |
| `Notifications__Email__*` | SMTP `Host`, `Port`, `UseSsl`, `Username`, `Password`, `FromAddress` |
| `Notifications__Sms__*` | HTTPS webhook `Endpoint`, `ApiKey`, `SenderId` |

Relative document/key paths resolve against the application content root. Development uses console notification senders. Outside Development, SMTP and SMS configuration is validated at startup; defaults are intentionally incomplete. There is no `.env` loader. Keep databases, keys, ID documents and generated outputs out of Git and outside public static files.

## Database and backend handoff

`Data/ApplicationDbContext.cs` and `Data/Migrations` define the actual schema. `Data/DbInitializer.cs` is a Development fixture loader with comments explaining ownership, initialization order, status boundaries, and backend integration intent. It is not a schema generator or production data import.

| UI/data area | Persistence and relationships |
|---|---|
| Identity/profile/IDs | `ApplicationUser`; `PatientIdDocument.PatientUserId` owns document metadata; encrypted files live outside SQLite |
| Visits/history | `ClinicalEncounter.PatientUserId`; `EncounterType` groups OPD/teleconsultation, emergency and inpatient |
| Laboratory | Patient-owned `LabResult`, optional `ClinicalEncounterId`, child `LabResultItem` rows |
| Medications | Patient-owned `Prescription`, child `MedicationDoseSchedule`, separate `PatientAllergy` |
| Malasakit application | One `SubsidyApplication` per patient; patient-reported payment/checklists are separate from authoritative outcomes |
| Retained triage | Owned legacy `Appointment`, at most one `TriageIntake` per appointment; self-reported symptoms/vitals, local storage only |
| Public content | `Doctor`, `PublicAdvisory`, and retained `AssistanceProgram` catalog; OPD/department guides also contain static content |
| Access history | `AuditLog` written by actual application actions; no fictitious audit rows are seeded |

Legacy appointment and refill columns/entities remain to preserve stored history and migration compatibility. Their presence does not enable booking or refills. No destructive schema migration is needed for this cleanup. Existing development rows are retained; updated fixture content applies to newly initialized data, not a forced rewrite of old records.

Latest migration: `20260916095804_RemoveOpdQueue`. Keep all historical migrations. The restore-encounters migration is forward-only. Back up the database and encryption keys before upgrades.

Production does not run DbInitializer or automatically migrate. Install a matching EF CLI tool (if needed: `dotnet tool install --global dotnet-ef --version 10.0.11`), configure the target connection, then apply migrations before starting the application:

```powershell
dotnet ef database update --project src/DrmcPatientPortal
```

See [deployment and operations](docs/DEPLOYMENT.md) for persistent storage, notifications and release checks. Institution-approved source data, adapters, hosting controls and sign-off are still required for production use.

## Validation and developer notes

```powershell
# Backend tests, including fresh SQLite migration/seeding and repeat-start preservation
dotnet test tests/DrmcPatientPortal.Tests -c Release

# Build, install browsers, run both test projects
pwsh scripts/test-ui.ps1

# Subsequent runs may reuse installed browsers
pwsh scripts/test-ui.ps1 -SkipBrowserInstall

# Dependency advisory check
dotnet list DrmcPatientPortal.slnx package --vulnerable --include-transitive
```

The browser fixture starts an isolated Development server on a free port with a temporary database, document directories and key ring. It exercises desktop/mobile navigation, forms, consent, registration, 2FA, filters, languages, document handling, retained workflows and accessibility. Results/screenshots are written to ignored `output/playwright`. There is no separate JavaScript compilation or lint script; C# and Razor compile during the solution build. Do not run a server from the same build output being rebuilt on Windows, where running assemblies are locked.

Keep owner-scoped queries, antiforgery validation, encrypted storage and audit checks when integrating a backend. Add migrations for actual schema changes; do not rename database entities merely to match navigation text. Public HTML advisory content requires a trusted publishing workflow. Never describe locally saved clinical or subsidy state as approved, verified, synchronized or dispensed without a real institutional confirmation.

[Design conventions](docs/design.md), [OPD content provenance](docs/opd-guide-content-sources.md), and [deployment requirements](docs/DEPLOYMENT.md) remain useful handoff references. Dated requirement reports and screenshots record prior reviews; the running application and current source take precedence over historical documents.
