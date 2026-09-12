# DRMC Patient Portal

ASP.NET Core 10 patient portal for Davao Regional Medical Center. It provides public appointment booking, queue and hospital information, plus authenticated access to laboratory results, encounters, medications, triage, and access history.

## Current scope

| Area | Access | Current behavior |
|---|---|---|
| Appointments | Public booking; protected follow-up | Confirmation, check-in, and cancellation require the authenticated owner or a 256-bit capability delivered by email/SMS/QR. Only the SHA-256 hash is stored; access expires 24 hours after the visit. |
| Queue | Public | Serving, called, and waiting values refresh on demand and every 30 seconds while visible. |
| Encounters | Authenticated | Patient-owned list, details, linked labs, printable summary, and fail-closed auditing. |
| Laboratory results | Authenticated | Patient-owned results, details, trends, printable report, and access auditing. |
| Medications | Authenticated | Active prescriptions, persisted dose times, and local refill requests. New requests remain `Requested` pending institutional pharmacy review. |
| Triage | Authenticated | Validated, one-per-appointment intake. Emergency red flags persist as `UrgentEmergency` before the warning is shown. |
| ID documents | Authenticated owner | Verified JPEG/PNG, 10 MB limit, encrypted storage, signed 30-minute staging tokens, ownership checks, and plaintext legacy migration. |
| Localization | Shared shell and restored clinical flows | English (`en`), Filipino (`fil`), and Cebuano (`ceb`). Patient-entered and clinical-record content is intentionally not translated. |

Messaging and caregiver/proxy access are not in the current product. Historical phase documents mentioning them are retained as project history and marked superseded.

## Technology

- .NET 10, ASP.NET Core MVC and Identity
- Entity Framework Core 10 with SQLite
- Bootstrap 5 and locally vendored Bootstrap Icons
- xUnit, Moq, EF Core InMemory, and Playwright CLI verification
- Tesseract OCR and ASP.NET Core Data Protection

## Run locally

```powershell
dotnet restore DrmcPatientPortal.slnx
dotnet ef database update --project src/DrmcPatientPortal/DrmcPatientPortal.csproj
dotnet run --project src/DrmcPatientPortal/DrmcPatientPortal.csproj
```

Development startup applies migrations and idempotently seeds local data.

| Account | Password | Purpose |
|---|---|---|
| `patient@drmc.doh.gov.ph` | `P@tient2026` | Patient with appointments, encounters, results, and prescriptions |
| `juan@drmc.doh.gov.ph` | `J@uan2026` | New-patient workflow checks |

Never copy development seed credentials into a deployed environment.

## Routes

### Public and appointment routes

| Route | Purpose |
|---|---|
| `GET /`, `/OpdGuide`, `/Directory`, `/Malasakit`, `/Advisories` | Public services and information |
| `GET /Queue` and `GET /Queue/Live` | Queue board and polling data |
| `GET/POST /Appointments/Book` | Public booking |
| `GET /Appointments/Access` | URL token exchange into an encrypted HttpOnly access cookie |
| `GET /Appointments/Confirmation` | Owner/capability-protected confirmation |
| `GET /Appointments/CheckIn` | Owner/capability-protected check-in |
| `POST /Appointments/Cancel` | Owner/capability-protected cancellation and capability revocation |

### Authenticated patient routes

| Route | Purpose |
|---|---|
| `GET /Patient/Home` | Patient dashboard |
| `GET /Patient/Encounters` | Encounter timeline |
| `GET /Patient/Encounters/Details/{id}` | Owned encounter with linked labs |
| `GET /Patient/Encounters/Print/{id}` | Printable owned encounter summary |
| `GET /Patient/LabResults` | Laboratory results and trends |
| `GET /Patient/Medications` | Prescriptions, exact schedules, and refill status |
| `GET/POST /Patient/Triage/*` | Pre-consultation intake |
| `GET /Patient/Audit` | Patient access history |
| `GET /Patient/Documents/IdPhoto/{id}` | Audited, owner-only decrypted document |

## Data and security

Migration `20260912091834_RestoreEncountersAndHardenPortal` is forward-only. It restores `ClinicalEncounters` and `LabResults.ClinicalEncounterId`, and adds appointment capability fields, `MedicationDoseSchedules`, document MIME/storage metadata, and a unique appointment-to-triage constraint.

- Identity locks accounts for 15 minutes after five failed attempts.
- PHI audits fail closed and return a localized generic 503 on audit failure.
- QR payloads contain only a capability URL, never patient identity or complaint data.
- Security headers include self-only CSP, `nosniff`, `SAMEORIGIN`, and `no-referrer`.
- ID files and staging files are encrypted, server-named, and canonically resolved.
- Console notifications are Development-only; Production validates SMTP and SMS configuration at startup.

See [deployment and operations](docs/DEPLOYMENT.md) and the [security review register](docs/SECURITY_REVIEW_TODO.md).

## Build and test

```powershell
dotnet build DrmcPatientPortal.slnx --no-restore
dotnet test DrmcPatientPortal.slnx --no-build
dotnet list DrmcPatientPortal.slnx package --vulnerable --include-transitive
```

Current result: **25/25 tests passing** on .NET 10 with zero build warnings. The OCR image-generation fixture is explicitly marked Windows-only because it uses `System.Drawing`; production code is platform-neutral. NuGet reports no known vulnerable packages.

## Browser validation

For repeatable desktop/mobile UI validation, run `pwsh scripts/test-ui.ps1` (or add `-SkipBrowserInstall` after the first run). The browser suite starts a separate development server with a temporary SQLite database, key ring, and document directories. It never uses the normal `app.db`. Screenshots, device metadata, accessibility findings, and TRX results are written under the ignored `output/playwright/` directory. See [UI validation](docs/UI_VALIDATION.md) for coverage and limitations.

## External boundaries

No DRMC HIS, pharmacy, SIEM, SMTP, or SMS credentials are stored here. HIS and pharmacy synchronization remain disabled until approved adapters and credentials exist. Local triage and refill states never claim external synchronization or dispensing.

## Documentation

- [Current status](STATUS.md)
- [Deployment](docs/DEPLOYMENT.md)
- [Security review](docs/SECURITY_REVIEW_TODO.md)
- [Compliance readiness](docs/COMPLIANCE_REPORT.md)
- [Design](docs/design.md) and [UX notes](docs/ux-notes.md)
- [Historical Phase 3 plan](docs/PHASE3_PLAN.md)
