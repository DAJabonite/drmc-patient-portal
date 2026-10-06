# DRMC Patient Portal

ASP.NET Core MVC and Identity application for Davao Regional Medical Center. Public pages provide hospital information, doctor listings and advisories. Patients can register, manage encrypted ID documents and view their linked hospital records, including the availability of laboratory results and radiology (imaging) studies. The staff Admin area manages patient records, clinical availability, public content and reconciled imports.

This application is not connected to hospital clinical, pharmacy, billing or social-work systems. Demonstration records, schedules and guidance require institutional approval before production use.

## Prerequisites

- .NET 10 SDK and Entity Framework Core command-line tool 10.0.11.
- SQL Server; SQL Server Express with Windows authentication is suitable for local development.
- Windows when opting into current-user DPAPI protection.
- Writable, persistent directories for encryption keys, encrypted patient documents and import staging, outside Git and the web root.
- SMTP and HTTPS SMS delivery configuration outside Development. Development sends delivery details to the console.

The application uses EF Core 10, SQL Server, Bootstrap 5, Razor Pages, Identity, HtmlSanitizer and ExcelDataReader. Local OCR uses Tesseract; authenticator enrollment uses QRCoder.

## Local Setup

From the repository root, restore and build:

```powershell
dotnet restore DrmcPatientPortal.slnx
dotnet build DrmcPatientPortal.slnx -c Release
dotnet tool install --global dotnet-ef --version 10.0.11
```

Use the installed matching tool if it is already available. Development configuration targets `localhost\SQLEXPRESS`, database `DrmcPatientPortal_Dev`, with integrated authentication. Deployment configuration contains placeholders. Environment variables use double underscores for nested settings; keep credentials in user-secrets or a deployment secret store, never in tracked configuration.

Before applying migrations, check the resolved connection, including any environment or user-secret overrides. For local development, use only the following target:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__DefaultConnection = 'Server=localhost\SQLEXPRESS;Database=DrmcPatientPortal_Dev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$target = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($env:ConnectionStrings__DefaultConnection)
Write-Host "Migration target: server=$($target.DataSource); database=$($target.InitialCatalog)"
if ($target.DataSource -ne 'localhost\SQLEXPRESS' -or $target.InitialCatalog -ne 'DrmcPatientPortal_Dev') { throw 'Unexpected database target' }
dotnet ef database update --project src/DrmcPatientPortal -- --environment Development
dotnet run --project src/DrmcPatientPortal --launch-profile http
```

Open `http://localhost:5095`. Startup requires a reachable, fully migrated database in every environment; migrations are not applied automatically. No database reset is needed for ordinary startup. SQL Server creates the schema with `Latin1_General_100_BIN2` collation; an existing empty database must already use that collation. The initial migration does not transfer SQLite data.

### macOS, Linux, or Docker SQL Server

Without SQL Server Express, run SQL Server Developer edition from `compose.yaml` (requires Docker; Apple silicon uses amd64 emulation):

```bash
export MSSQL_SA_PASSWORD='<local-only strong password>'
docker compose up -d --wait
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__DefaultConnection="Server=localhost,1433;Database=DrmcPatientPortal_Dev;User Id=sa;Password=$MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"
dotnet ef database update --project src/DrmcPatientPortal -- --environment Development
dotnet run --project src/DrmcPatientPortal --launch-profile http
```

The container binds to `127.0.0.1` only and keeps data in the `drmc-sqlserver` volume; `docker compose down -v` deletes it. Use this password for local development only.

### Configuration

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | SQL Server connection |
| `DataProtection__KeyRingPath` | Persistent key directory; default `App_Data/DataProtectionKeys` |
| `DataProtection__ProtectKeysWithDpapi` | Opt-in Windows current-user protection; default `false` |
| `PatientDocuments__RootPath` | Encrypted patient ID document directory |
| `PatientDocuments__TemporaryPath` | Temporary encrypted registration uploads |
| `PatientDocuments__MaximumFileSizeMb` | Document upload limit; default 10 MB |
| `PatientDocuments__TemporaryFileLifetimeMinutes` | Temporary upload lifetime; default 30 minutes |
| `Imports__StagingPath` | Encrypted import staging; default the current user's local application-data DRMC/ImportStaging directory |
| `Notifications__Email__*` | SMTP host, port, TLS, credentials and sender |
| `Notifications__Sms__*` | HTTPS webhook, API key and sender ID |
| `PatientResults__ShowFullResults` | Show released laboratory values and radiology reports to the owning patient; default `false`. Keep it `false` in tracked settings until DRMC approves full results in the portal |
| `PatientRegistration__RequireHospitalRecordCode` | Require a hospital record code at signup; default `false`, so anyone can still register and be linked later by staff |
| `PatientRegistration__CodeLifetimeDays` | How long a hospital record code stays valid; default `7`, limited to 1–30 days |
| `Identity__RequireConfirmedAccount` | Require a confirmed email before sign-in; default `true`. In Development the confirmation link is written to the console |

Relative storage paths resolve from the application content root. Restrict directory permissions to the app identity and approved operators. Retain the key ring across releases and back it up together with patient documents and the database.

### Database Migrations

Back up the database before applying or reversing migrations. New migrations are:

1. `20261004153506_PatientRegistryOwnership`: moves clinical ownership to independent hospital patient records. Existing clinical IDs, values, relationships and account ownership are preserved. One linked record is created per distinct clinical owner; no hospital numbers or birth dates are invented. Empty names abort the migration. Accounts without clinical data remain unlinked. Downgrade aborts if clinical rows belong to unlinked patient records.
2. `20261004162211_AdminConcurrencyAudit`: adds concurrency tokens and staff audit storage. Downgrade removes staff history and concurrency columns; do not use it on a database whose history must be retained.
3. `20261005042232_AdminImportBatches`: stores operational import-job metadata. Downgrade is blocked when import history exists.
4. `20261005070928_AdminImportLeases`: adds nullable worker ownership and lease fields plus a status/lease index, without rewriting existing rows. Downgrade is blocked when import history exists.
5. `20261006024312_AddRadiologyStudies`: adds the `RadiologyStudies` table (unique accession number ignoring case, owning patient record, optional encounter, concurrency token). No existing rows are changed. Downgrade is blocked when radiology studies exist.
6. `20261006164919_AddPatientRegistrationCodes`: adds the `PatientRegistrationCodes` table (owning patient record with cascade delete, unique SHA-256 code hash, 5-character hint, issuing point, issuer, issue, expiry, redemption and revocation times, concurrency token). No existing rows are changed. Downgrade is blocked when codes exist.

The initial SQL Server migration is `20260929123803_InitialSqlServer`. During upgrade, drain and stop older builds, apply migrations, then run only the new build. Older workers cannot respect the lease protocol.

## Testing

`tests/DrmcPatientPortal.Tests` holds integration tests that boot the app with `WebApplicationFactory`. Each run creates a uniquely named SQL Server database, applies migrations, seeds synthetic fixtures and drops it afterwards. Point the tests at a server whose login may create databases, without a database name:

```bash
export DRMC_TEST_SQLSERVER="Server=localhost,1433;User Id=sa;Password=$MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"
dotnet test DrmcPatientPortal.slnx
```

The tests cover public pages, the sign-in requirement, patient record isolation, the not-found page, the Content Security Policy, email confirmation, readable lab categories, the Admin access matrix for Admin, LabStaff, RadiologyStaff and PatientServicesStaff, hospital record codes (format, issuing, single use, concurrency, expiry, revocation, birth-date match and required mode), staff role grants, radiology Admin CRUD, the patient Radiology page and navigation, and the `PatientResults:ShowFullResults` switch in both positions. GitHub Actions (`.github/workflows/ci.yml`) runs the build with warnings as errors, a pending-migration check and these tests on every pull request against a SQL Server service container.

Views must not use inline `<script>` blocks or `on*` attributes, because the Content Security Policy only allows `script-src 'self'`. Put scripts in `wwwroot/js`; use `data-auto-submit` on a form control or `data-confirm="..."` on a form for the common cases.

## Development Fixtures

Fixtures are opt-in, Development-only and have no default emails or passwords. Configure all four credentials locally when initializing an empty schema:

```powershell
$env:DevelopmentFixtures__Enabled = 'true'
$env:DevelopmentFixtures__Primary__Email = Read-Host 'Synthetic populated account email'
$env:DevelopmentFixtures__Primary__Password = Read-Host 'Synthetic populated account password'
$env:DevelopmentFixtures__Empty__Email = Read-Host 'Synthetic empty account email'
$env:DevelopmentFixtures__Empty__Password = Read-Host 'Synthetic empty account password'
dotnet run --project src/DrmcPatientPortal --launch-profile http
```

Alternatively, run `dotnet user-secrets init --project src/DrmcPatientPortal` and set the same colon-separated keys with `dotnet user-secrets set --project src/DrmcPatientPortal`. Remove the fixture settings after initialization.

Stable fixture account IDs are retained. Seeding runs transactionally only when all business tables, both audit histories and import history are empty. It skips the entire batch when data exists; it never restores deleted records, overwrites staff changes, resets existing passwords or repairs relationships.

## First Admin

1. Apply migrations, start the Development app and register an account using Identity registration. Bootstrap never creates an account or password.
2. Confirm the email using the Development confirmation link printed in the console, or the configured delivery service outside Development.
3. In account management, enroll an authenticator and enable two-factor authentication.
4. Set the following variables for that existing account and restart:

```powershell
$env:AdminBootstrap__Enabled = 'true'
$env:AdminBootstrap__Email = Read-Host 'Confirmed staff account email'
dotnet run --project src/DrmcPatientPortal --launch-profile http
```

5. Remove both bootstrap variables, restart, sign out and sign in with an authenticator code. Open `/Admin`.

The Admin, LabStaff and RadiologyStaff roles are created idempotently at startup. Bootstrap only promotes an existing, email-confirmed, 2FA-enabled, non-locked-out account. Every startup while bootstrap is enabled logs a warning, including when no email is configured.

All Admin endpoints require a staff role allowed for that section (see Staff Roles), currently confirmed email, enabled 2FA, a non-locked-out account and an `amr=mfa` sign-in claim. Password-only and remembered-browser sessions do not qualify. Anonymous requests receive the normal Identity login challenge with `ReturnUrl`; authenticated requests failing these rules receive HTTP 403. Select **Forget this browser** under Two-factor authentication, then sign out and sign in with an authenticator code. Admin writes require antiforgery tokens. Startup rejects anonymous Admin endpoints and Admin controllers without a registered staff policy.

## Staff Roles

| Role | Admin sections |
| --- | --- |
| Admin | Everything, including `/Admin/StaffAccess` |
| LabStaff | Dashboard, `/Admin/LabResults`, `/Admin/LabResultItems` |
| RadiologyStaff | Dashboard, `/Admin/RadiologyStudies` |
| PatientServicesStaff | Dashboard, `/Admin/RegistrationCodes` (PACD and clinic desks) |

To add staff: the person registers, confirms their email and enables two-factor authentication. An Admin opens `/Admin/StaffAccess`, selects the account and grants LabStaff, RadiologyStaff or PatientServicesStaff. Admin itself is only granted through bootstrap. Grants and revocations take effect on the next request, refresh the account's security stamp and are written to the staff audit log. Staff see only their sections in the sidebar and dashboard; the dashboard hides the audit feed and import status from non-Admin staff. The portal's Administration link appears after the person signs in again. Staff choose patients through the existing patient context picker, which shows name and hospital number only.

## Admin Area

| Page | Purpose |
| --- | --- |
| `/Admin/Patients` | Hospital registry and separately confirmed portal links |
| `/Admin/RegistrationCodes` | Issue, print and revoke single-use hospital record codes for portal signup |
| `/Admin/Doctors` | Public doctor directory |
| `/Admin/PublicAdvisories` | Sanitized public advisories |
| `/Admin/ClinicalEncounters` | Dashboard and visit records |
| `/Admin/LabResults` | Laboratory availability |
| `/Admin/LabResultItems` | Items within a selected lab (patients see them only when `PatientResults:ShowFullResults` is on) |
| `/Admin/RadiologyStudies` | Imaging studies, reports, release time and staff-only internal notes |
| `/Admin/StaffAccess` | Grant or revoke LabStaff, RadiologyStaff and PatientServicesStaff (Admin only) |
| `/Admin/Prescriptions` | Medications and refills |
| `/Admin/MedicationDoseSchedules` | Dose times within a selected prescription |
| `/Admin/PatientAllergies` | Patient allergies |
| `/Admin/Audit` | Read-only staff access and change history |
| `/Admin/Imports` | Templates, uploads, reconciliation and job status |

Lists use server-side search, fixed ordering and 25-row pages. Clinical forms show patient and parent context; edits cannot move records between patients or parents. Stale edits and deletes require review. Deletes with dependent records are blocked with counts. Hospital numbers retain leading zeros, are optional and unique ignoring case when present, and are never generated. Physician choices are stored as text snapshots.

### Account Linking and Audit

Hospital patient records exist independently of portal accounts. Staff verify identity, select one unambiguous email-confirmed account and confirm the check through a dedicated link form. Names, birth dates and similar emails are never automatic identity proof. Unlinking removes clinical access immediately; deleting an account preserves the hospital record and its clinical relationships. Patient ID documents remain account-owned.

Changes and staff audit entries commit together. Registry and clinical audit entries retain field names only, not earlier medical values; they cannot reconstruct prior records. Doctors and PublicAdvisories retain old/new values. Staff clinical views log affected patient references before display; failed audit persistence prevents display. The patient activity page remains unchanged. Both audit tables are append-only through application guards and have no edit/delete endpoints. Database access controls and retention procedures remain an operational responsibility.

General Identity administration, sensitive ID-document metadata, audit tables, migration history and import system metadata have no generic editor. Laboratory item values and result summaries are staff-only by default; when `PatientResults:ShowFullResults` is switched on, released results (and released radiology reports) are shown to the owning patient, and clinical and internal notes stay staff-only. The patient laboratory claiming guide is retained. New advisory saves sanitize HTML, but existing legacy HTML is not rewritten; review it before publication. The existing inline-script CSP allowance is unchanged.

## Hospital Record Codes

A hospital record code shows that a person has a record at DRMC and links that record to the portal account they create, without a separate staff linking step. The design follows the MyChart activation-code pattern and NIST SP 800-63A enrollment-code guidance. See [the plan](docs/design/hospital-record-code-plan.md).

1. At the PACD or a clinic desk (for example the Red Star Clinic), Patient services staff or an Admin check the patient's valid ID against the hospital record. They then open **Registration codes › Issue code**, or **Registration code** on the patient's details page.
2. The record must be unlinked and must have a birth date. Staff choose the issuing point, confirm the identity check, and print or show the slip. The slip has the code in the form `XXXXX-XXXXX-XXXXX` and a QR code. The full code is shown only once and is never stored: the database keeps a SHA-256 hash and the last five characters. Issuing a new code revokes any earlier active code for that record. Each issue and revoke is written to the staff audit log.
3. The QR opens `/Identity/Account/Register#code=…`. The code is in the URL fragment, which browsers do not send to the server. On the **Credentials** step, the **Hospital record code** field is filled in from the QR, typed by the patient, or scanned with the camera where the browser supports `BarcodeDetector`. Input is not case-sensitive and ignores spaces and hyphens.
4. Signup checks the code before creating the account. The code must be active (not used, revoked or expired), its record must still be unlinked, and the date of birth entered must match the record. If any check fails, signup shows one generic message and creates no account. On success, the code is marked used and the record is linked in a single serializable transaction, and `LINK_HOSPITAL_RECORD` is written to the patient activity log. If a later signup step fails, the account is removed and the code is released so it can be used again.

Codes are optional unless `PatientRegistration:RequireHospitalRecordCode` is `true`. Accounts created without a code can still be linked by an Admin through **Verified portal link**.

## Patient Radiology

`/Patient/Radiology` lists the signed-in patient's own imaging studies with imaging-type, date and search filters, a preparation guide and an overview of DRMC imaging services. A study is "Ready for claiming" once its status is Final or Amended and its release time (Manila time) has passed; otherwise it shows "Report in progress". Report sections and the plain-language summary are shown only for released studies and only when `PatientResults:ShowFullResults` is on. Another patient's study returns 404. Internal notes never reach patient pages. The portal shows no images; films and CDs are claimed from DRMC Radiology. Each detail view is recorded as `VIEW_RADIOLOGY_REPORT` in the patient activity log.

To try full results locally, set `$env:PatientResults__ShowFullResults = 'true'` before `dotnet run`. Do not commit it.

## CSV and Excel Imports

Download versioned CSV headers or the nine-sheet workbook from `/Admin/Imports`. CSV targets one `*_v1` template; `.xlsx` targets `Workbook_v1` and fixed named sheets. Preserve the supplied column order. Unknown columns, sheets, Identity fields and arbitrary mappings are rejected. See [Import Templates](docs/import-templates.md) for columns and value formats.

Staff reconcile every source patient group to an existing hospital record or explicitly approve creating a new one. Source keys, names and birth dates are review aids, not automatic matching rules. Child rows also reference a source parent. Imports never link portal accounts, merge records, update existing rows or skip duplicates.

Upload, save mappings, queue a dry run, review errors and proposed counts, then queue approval. Dry run, approval and execution run on the durable worker; requests return to a polling status page. Dry runs write no clinical rows. Approval binds the file hash, mappings and validation version; changes require another dry run. Validation loads existing keys and relationships in bounded set-based queries, and execution checks them again within its transaction. The initiating staff account must remain eligible throughout.

### Limits and Recovery

- Maximum 20 MB and 10,000 data rows across all sheets per batch; preview and reconciliation pages show 25 rows/groups.
- CSV is strict UTF-8. Workbooks must contain values only; encrypted/OLE files, macros, formulas, defined-name formulas and external links are rejected before parsing.
- Archive limits: 256 entries, 80 MB expanded total and 40 MB per entry. Hospital numbers and source identifiers must use text cells.
- Whole-batch business records and per-record audit entries commit atomically. An error rolls everything back; imports are create-only and duplicates block the batch.
- Queued phases survive restart. Running phases with expired leases fail without replay; live leases are never recovered. Failed uploads must be reviewed, cancelled and uploaded again.
- Queued phases can be cancelled; running phases cannot. Success and cancellation purge staged content. Unfinished batches expire seven days after upload; operational metadata remains.

Two independently started application processes are supported for overlapped recycle or redeploy, not general horizontal scaling. They must share the same build, database, Windows identity, protection setting, persistent key ring and staging directory. Execution is single-flight across both processes; dry run and approval may overlap. Claims use conditional status/rowversion updates, unique process owners and 60-second leases renewed about every 15 seconds. Completion is fenced by owner, rowversion and a live lease inside the business/audit transaction. A lost lease cannot commit records. No automatic replay or uncertain-commit retry is performed.

For IIS, use persistent shared directories and enable Load User Profile when using current-user DPAPI. Keep the app-pool identity/profile stable. Worker polling is every two seconds; status polling is every three seconds, pauses while the page is hidden and announces status changes through a polite live region.

## Time-Zone Rules

Staff-entered clinical dates/times are Manila wall time stored as `datetime2` without an offset. Patient date filters use explicit Manila today. Advisory publication/effectivity dates and system, audit and import timestamps are UTC. Browser date/time fields preserve additional stored fractional precision when unchanged. Existing clinical timestamps are not shifted; their original time-zone meaning still needs confirmation before any conversion.

## Key Protection and Recovery

`DataProtection:ProtectKeysWithDpapi` defaults to `false`, pending the client's key-protection decision. With the setting off, existing plaintext keys are not converted and new keys remain unprotected at rest, as in the previous release. Non-Development startup warns that keys are unprotected and points here. Directory permissions and protected backups are essential. A protected key ring cannot be used with the setting off: startup rejects it rather than generating a mixture of protected and plaintext keys.

To explicitly opt in on Windows, set `DataProtection__ProtectKeysWithDpapi=true`. Startup converts existing plaintext key secrets atomically, verifies conversion, preserves key IDs and creates no plaintext backup; new keys also use current-user DPAPI. Conversion errors and unsupported platforms fail startup. Turning this on ties the keys to that Windows account and machine. Keep it enabled afterward; switching the flag off does not decrypt or revert keys. Do not run older builds or processes using different settings during conversion.

The same key ring encrypts patient ID documents and temporary document uploads as well as import staging and commands, cookies and tokens. Losing historical keys can make existing documents and unfinished imports unreadable. Copying current-user DPAPI key XML to another machine or account is not a usable recovery method; retain the original recovery identity/profile until a compatible recovery plan is established. Do not reset unreadable import jobs to bypass decryption failures.

The client must choose and approve a protection and backup strategy:

| Option | Operational decision |
| --- | --- |
| Certificate-based protection | Securely back up the private key, grant approved app identities access, retain historical keys and test document/import recovery on the destination machine. Requires a separate implementation. |
| DPAPI-NG with an AD group | Use a supported domain setup and approved group descriptor; review membership, domain recovery and access controls. Requires a separate implementation. |
| Accept the risk | Explicitly accept plaintext keys protected only by storage controls, or opt into machine/account-bound DPAPI and accept its recovery limitations. Document the choice and recovery responsibilities. |

No portable recovery option is implemented. Any future change must preserve decryption of existing patient documents and queued imports before retiring the old identity. Encryption does not protect against a compromised application identity or database/storage administrator.

## Known Limitations and Client Inputs

Representative source files and expected dataset volume are still needed. Fixed templates do not promise compatibility with arbitrary legacy spreadsheets. Batch limits are safety bounds, not performance guarantees; execution holds one serializable transaction for the whole batch and other writers may cause an atomic failure.

Confirm the meaning of existing clinical timestamps, approve audit/import/document retention and establish key backup/recovery ownership. Radiology needs to confirm the patient preparation guide, the films/CD and report releasing window, hours and claiming procedure, and whether the Basement 1 location and Local 208 shown on patient pages are current; the portal marks these as to be confirmed and shows no images. Approve whether patients may see full results before switching `PatientResults:ShowFullResults` on. The Filipino and Cebuano "Radiology" navigation labels need translator review. Departments, OPD and Malasakit guides, privacy/terms, official links and facility instructions remain hard-coded; decide whether they should become managed content. Clinical integrations, authoritative content and institutional deployment approval remain outside this application's current scope.
