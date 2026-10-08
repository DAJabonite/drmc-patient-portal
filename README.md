# DRMC Patient Portal

ASP.NET Core MVC and Identity application for Davao Regional Medical Center. Public pages provide hospital information, doctor listings and advisories. Patients can register, manage encrypted ID documents and view their linked hospital records, including the availability of laboratory results and radiology (imaging) studies. The staff Admin area manages patient records, clinical availability, public content and reconciled imports.

This application is not connected to hospital clinical, pharmacy, billing or social-work systems. Demonstration records, schedules and guidance require institutional approval before production use.

Start with [Local Setup](#local-setup), then [First Admin](#first-admin), [Staff Invitations](#staff-invitations) and the [Admin Management Walkthrough](#admin-management-walkthrough). Developers can also use [Testing](#testing), [Configuration](#configuration) and [Database Migrations](#database-migrations); deployment operators should review [Server Hosting and Handover](#server-hosting-and-handover) and [Key Protection and Recovery](#key-protection-and-recovery).

## Prerequisites

- Git, the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), and Entity Framework Core command-line tool 10.0.11.
- SQL Server; SQL Server Express with Windows authentication is suitable for local development. Install [SQL Server Management Studio (SSMS)](https://learn.microsoft.com/en-us/ssms/install/install) separately to create databases and explore tables.
- An authenticator app for Admin and staff two-factor authentication (2FA).
- Windows when opting into current-user DPAPI protection.
- Writable, persistent directories for encryption keys, encrypted patient documents and import staging, outside Git and the web root.
- SMTP and HTTPS SMS delivery configuration outside Development. Development sends delivery details to the console.

The application uses EF Core 10, SQL Server, Bootstrap 5, Razor Pages, Identity, HtmlSanitizer and ExcelDataReader. Local OCR uses Tesseract; authenticator enrollment uses QRCoder.

## Local Setup

Follow these steps for a fresh Windows development installation. Use a dedicated portal database and fictional records for the walkthrough. Existing installations should back up their database, encryption keys and documents before applying updates; ordinary startup does not require deleting anything.

### 1. Install SQL Server and SSMS

These are separate tools:

| Tool | What it does |
| --- | --- |
| SQL Server Express | Runs the database engine and stores databases |
| SQL Server Configuration Manager | Manages SQL services and network settings |
| SSMS | Connects to the engine, creates databases, browses tables and runs SQL queries |

If SQL Server Express is already running, keep that instance. Otherwise download Express from [Microsoft SQL Server downloads](https://www.microsoft.com/en-us/sql-server/sql-server-downloads). For a configurable installation, choose **Custom**, then a new standalone installation with **Database Engine Services**, instance name **SQLEXPRESS**, **Windows authentication**, and your Windows user added as a SQL administrator for local development. See [Microsoft's installation walkthrough](https://learn.microsoft.com/en-us/sql/database-engine/install-windows/install-sql-server-from-the-installation-wizard-setup).

Install SSMS from [Microsoft's official installer page](https://learn.microsoft.com/en-us/ssms/install/install). Run `vs_SSMS.exe`, complete installation, then **Launch**. Optional workloads are unnecessary for this walkthrough. If the page fails in an embedded browser, try Edge or Chrome, or use the page's [official installer link](https://aka.ms/ssms/22/release/vs_SSMS.exe). The installer uses Visual Studio Installer; the Visual Studio IDE is not required.

In SSMS, connect with:

| Connection field | Local value |
| --- | --- |
| Server name | `localhost\SQLEXPRESS` |
| Authentication | Windows Authentication |
| Database name | Default |
| Encrypt | Mandatory |

If connection fails with **The certificate chain was issued by an authority that is not trusted**, enable **Trust Server Certificate** and reconnect to this local instance. This keeps encryption enabled while skipping server certificate verification; see [Microsoft's certificate guidance](https://learn.microsoft.com/en-us/troubleshoot/sql/database-engine/connect/error-message-when-you-connect). A deployed server should use a trusted certificate and certificate validation.

Object Explorer should now show the server and its **Databases** folder. If the engine cannot be reached, confirm **SQL Server (SQLEXPRESS)** is running in Configuration Manager. SSMS alone does not install or run the database engine.

### 2. Create your own empty database in SSMS

1. Right-click **Databases → New Database**.
2. Enter **DrmcPatientPortal_Dev** as the database name.
3. Under **Options**, set **Collation** to **Latin1_General_100_BIN2**.
4. Leave the other settings at their defaults, select **OK**, and refresh **Databases**.

Alternatively, use **New Query** against `master` and run this once instead of using the dialog:

```sql
CREATE DATABASE [DrmcPatientPortal_Dev]
COLLATE Latin1_General_100_BIN2;
```

Verify the database you created:

```sql
USE [DrmcPatientPortal_Dev];

SELECT DB_NAME() AS DatabaseName,
       DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS CollationName;
SELECT name AS TableName FROM sys.tables ORDER BY name;
```

Expect the chosen name, `Latin1_General_100_BIN2`, and no application tables yet. Do not change the collation of a populated database to repeat this exercise. Each independent developer can use a distinct name, such as `DrmcPatientPortal_Dev_Alex`; replace it consistently in the SQL and PowerShell commands below.

### 3. Clone, restore and build

Open PowerShell in the parent folder where you want the source code:

```powershell
git clone https://github.com/DAJabonite/drmc-patient-portal.git
Set-Location drmc-patient-portal
git branch --show-current
git log -1 --oneline
```

A normal clone starts on `master`. Use a new empty destination if a previous copy exists. GitHub contains code and migrations, not your local database, accounts, secrets or encrypted uploads. Deleting a project folder does not delete a database stored by SQL Server.

From the repository root, check the tools, restore packages and build:

```powershell
dotnet --version
dotnet restore DrmcPatientPortal.slnx
dotnet build DrmcPatientPortal.slnx -c Release --no-restore -warnaserror
dotnet ef --version
```

Stop and resolve any restore or build errors before continuing. If the EF tool is missing, run `dotnet tool install --global dotnet-ef --version 10.0.11`; if another version is installed, use `dotnet tool update --global dotnet-ef --version 10.0.11`. Confirm the version afterward.

`DrmcPatientPortal.slnx` is the XML solution file grouping the application and `tests/DrmcPatientPortal.Tests`. **Restore** downloads each project's NuGet dependencies from its `.csproj`; it does not restore or modify a database. Keep test source in GitHub so developers and CI can verify changes. Generated `bin/`, `obj/` and `TestResults/` output is already ignored. No npm build step is needed for the checked-in browser assets.

### 4. Configure this terminal and apply migrations

Run from the repository root. These environment variables apply to processes launched from this PowerShell window; repeat this block in a new window before running the portal. Set the database name to the one you created in SSMS:

```powershell
$databaseName = 'DrmcPatientPortal_Dev'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:AdminBootstrap__Enabled = 'false'
$env:DevelopmentFixtures__Enabled = 'false'
$env:ConnectionStrings__DefaultConnection = "Server=localhost\SQLEXPRESS;Database=$databaseName;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"

# Quiet database queries while keeping [EMAIL OUTBOX] messages and warnings.
$env:Logging__LogLevel__Microsoft = 'Warning'

# Give this installation persistent storage outside Git and the web root.
$localStorage = Join-Path $env:LOCALAPPDATA "DRMC\PatientPortal\$databaseName"
$env:DataProtection__KeyRingPath = Join-Path $localStorage 'DataProtectionKeys'
$env:PatientDocuments__RootPath = Join-Path $localStorage 'PatientIdDocuments'
$env:PatientDocuments__TemporaryPath = Join-Path $localStorage 'TempUploads'
$env:Imports__StagingPath = Join-Path $localStorage 'ImportStaging'

$target = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($env:ConnectionStrings__DefaultConnection)
Write-Host "Migration target: server=$($target.DataSource); database=$($target.InitialCatalog)"
if ($target.DataSource -ne 'localhost\SQLEXPRESS' -or $target.InitialCatalog -ne $databaseName) {
    throw 'Unexpected database target'
}

dotnet ef database update --project src/DrmcPatientPortal -- --environment Development
```

The explicit connection overrides Development user-secrets and tracked settings for this process. All clones under the same Windows user share the project's `UserSecretsId`; use per-terminal settings for independent installations. Environment variables use double underscores for nested settings. Store credentials outside Git; the connection above uses your Windows identity and needs no SQL password.

EF applies the committed migrations and creates the application tables. It can create a missing database itself when permitted, but the SSMS step lets you create and inspect it manually. Do not create another Initial migration or invent the portal tables. The initial migration does not transfer old SQLite data.

When EF reports `Done.`, refresh **Tables** in SSMS and verify:

```sql
USE [DrmcPatientPortal_Dev]; -- Use your chosen database name.

SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
SELECT COUNT(*) AS AccountCount FROM dbo.AspNetUsers;
SELECT COUNT(*) AS PatientRecordCount FROM dbo.PatientRecords;
```

This version has eight migrations, ending in `20261007035528_AddStaffInvitations`. A fresh installation has zero accounts and hospital patient records. Starting the app does not automatically apply migrations or seed demo data when fixtures are disabled.

### 5. Start the portal

Keep the same terminal open:

```powershell
dotnet run --project src/DrmcPatientPortal --launch-profile http
```

Open [http://localhost:5095](http://localhost:5095). Empty directory and clinical pages are expected until staff add records. Stop the running app with **Ctrl+C** before changing environment variables; an already-running process does not pick up those changes.

`--project src/DrmcPatientPortal` selects the application from the repository root. `--launch-profile http` selects the Development/HTTP settings in `src/DrmcPatientPortal/Properties/launchSettings.json`. If your current folder is already `src/DrmcPatientPortal`, `dotnet run` uses that project and its default applicable launch profile. `.\run.ps1` is another launcher; it does not create the schema.

For local HTTPS, run `dotnet dev-certs https --trust`, then use `--launch-profile https` and open `https://localhost:7104`. A phone's `localhost` points to the phone, so QR rehearsal on another device needs a reachable test HTTPS host and correct link origin.

Continue with [First Admin](#first-admin), then [Staff Invitations](#staff-invitations) and [Admin Management Walkthrough](#admin-management-walkthrough).

### macOS, Linux, or Docker SQL Server

Without SQL Server Express, run SQL Server Developer edition from `compose.yaml` (requires Docker and a compatible Linux x86-64 container host; ARM emulation is not a supported SQL Server deployment). See [Microsoft's container guidance](https://learn.microsoft.com/en-us/sql/linux/containers/deploy):

```bash
dotnet restore DrmcPatientPortal.slnx
dotnet build DrmcPatientPortal.slnx -c Release --no-restore -warnaserror
export MSSQL_SA_PASSWORD='<local-only strong password>'
docker compose up -d --wait
export ASPNETCORE_ENVIRONMENT=Development
export AdminBootstrap__Enabled=false
export DevelopmentFixtures__Enabled=false
export Logging__LogLevel__Microsoft=Warning
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
| `PatientRegistration__RequireHospitalRecordCode` | Require a hospital record code at signup; default `true`, so only patients with a DRMC hospital record can create an account. Set `false` only for a supervised rollout where staff link accounts manually |
| `PatientRegistration__CodeLifetimeDays` | How long a hospital record code stays valid; default `7`, limited to 1–30 days |
| `StaffInvitations__LifetimeDays` | How long an unused staff invitation stays valid; default `3`, limited to 1–14 days |
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
7. `20261007035528_AddStaffInvitations`: adds the `StaffInvitations` table (invited email and normalized email, staff role, unique SHA-256 code hash, 5-character hint, inviting Admin, creation, expiry, acceptance, role-grant and revocation times, accepting account, concurrency token). No existing rows are changed. Downgrade is blocked when invitations exist.

The initial SQL Server migration is `20260929123803_InitialSqlServer`. During upgrade, drain and stop older builds, apply migrations, then run only the new build. Older workers cannot respect the lease protocol.

## Testing

`tests/DrmcPatientPortal.Tests` holds integration tests that boot the app with `WebApplicationFactory`. Each run creates a uniquely named SQL Server database, applies migrations, seeds synthetic fixtures and drops it afterwards. Point the tests at a server whose login may create databases, without a database name:

For Windows SQL Server Express, run from the repository root:

```powershell
$env:DRMC_TEST_SQLSERVER = 'Server=localhost\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet test DrmcPatientPortal.slnx -c Release
```

For the Docker server above:

```bash
export DRMC_TEST_SQLSERVER="Server=localhost,1433;User Id=sa;Password=$MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"
dotnet test DrmcPatientPortal.slnx
```

The tests cover public pages, the sign-in requirement, patient record isolation, the not-found page, the Content Security Policy, email confirmation, readable lab categories, the Admin access matrix for Admin, LabStaff, RadiologyStaff and PatientServicesStaff, hospital record codes (format, issuing, single use, concurrency, expiry, revocation, birth-date match and required mode), staff invitations (email-bound single use, expiry, revocation, never granting Admin, role granted only after email confirmation and 2FA), first-Admin setup (only the bootstrap email, only until an Admin exists), staff role grants, radiology Admin CRUD, the patient Radiology page and navigation, and the `PatientResults:ShowFullResults` switch in both positions. GitHub Actions (`.github/workflows/ci.yml`) runs the build with warnings as errors, a pending-migration check and these tests on every pull request against a SQL Server service container.

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

Signup requires a hospital record code, and the first Admin has no one to issue one. While bootstrap is enabled and **no Admin exists yet**, the configured bootstrap email may sign up without a code. Every other email still needs a code or staff invitation. The exemption closes as soon as any account holds the Admin role.

### 1. Enable bootstrap for one email

Complete Local Setup first. Stop any running portal with **Ctrl+C**, then use the same PowerShell window with the database and storage settings still set:

```powershell
$env:AdminBootstrap__Enabled = 'true'
$env:AdminBootstrap__Email = Read-Host 'First Admin email'
dotnet run --project src/DrmcPatientPortal --launch-profile http
```

### 2. Register, confirm email and enable 2FA

1. Open `/Identity/Account/Register`, accept the privacy notice and terms, and complete the ID/identity steps. Manual entry is available; an ID photo is optional.
2. Register using **exactly the bootstrap email**. Leave **Hospital record code** empty. If the page still requires it, reload after starting with bootstrap enabled and check the configured email. Every other email continues to need a patient code or staff invitation.
3. In Development, find the newest **`[EMAIL OUTBOX]`** in PowerShell. Copy the complete URL inside the email's `href` quotes, excluding the quotes and surrounding HTML. Replace every **`&amp;` with `&`**, then paste into the browser. For example, `...?userId=123&amp;code=ABC` becomes `...?userId=123&code=ABC`. Real HTML email delivered through SMTP is encoded correctly as-is; email clients decode the separators when clicked.
4. Sign in after confirmation and open `/Identity/Account/Manage/TwoFactorAuthentication`. Set up Microsoft Authenticator, Google Authenticator or another TOTP app, scan the QR (or enter its key), and verify a current six-digit code.

Keep this session running until both email confirmation and 2FA are complete. Startup logs that bootstrap is waiting for the account; it never creates an account or password. The signup is written to the activity log as `FIRST_ADMIN_SIGNUP`.

If the link is invalid or incomplete, use **Request a new link** at `/Identity/Account/ResendEmailConfirmation` and copy the newest complete URL with the separators decoded. Quiet Microsoft logs using `$env:Logging__LogLevel__Microsoft = 'Warning'` before startup; `[EMAIL OUTBOX]` remains visible. If you need to restart while the account is unfinished, set `$env:AdminBootstrap__Enabled = 'false'` first. Finish confirmation and 2FA, then re-enable bootstrap for promotion.

### 3. Promote, disable bootstrap and sign in with MFA

Once email and 2FA are complete, stop the app. Re-enable bootstrap if you temporarily disabled it, confirm its email is still the registered address, and restart:

```powershell
$env:AdminBootstrap__Enabled = 'true'
$env:AdminBootstrap__Email = Read-Host 'Same registered first Admin email'
dotnet run --project src/DrmcPatientPortal --launch-profile http
```

Startup promotes the existing eligible account. After startup succeeds, stop again and run without bootstrap:

```powershell
$env:AdminBootstrap__Enabled = 'false'
Remove-Item Env:AdminBootstrap__Email -ErrorAction SilentlyContinue
dotnet run --project src/DrmcPatientPortal --launch-profile http
```

Remove any bootstrap settings also saved in user-secrets or deployment configuration. Sign out, then sign in using your password and an authenticator code; leave **Remember this device** unchecked. Open `/Admin`. If access is denied, use **Forget this browser** in 2FA management, sign out, and sign in with a fresh authenticator code.

### 4. Save recovery codes

After first-time authenticator setup, the portal automatically shows ten recovery codes. Save them privately before leaving the page; each code works once and the list is only shown once. For an existing account, open the account menu → **Two-Factor Auth (2FA)** → **Recovery Codes** and select **Generate Recovery Codes** (or **Generate New Recovery Codes**). Confirm on the next screen and save the new set. Generating replacements invalidates the previous set; reconfiguring the same authenticator preserves any codes you still have.

To use one, enter your email and password normally. At the authenticator challenge, select **Can't use your authenticator? Use a recovery code** and submit an unused code. Each code works once. It does not bypass current role, confirmation or lockout checks.

The Admin, LabStaff, RadiologyStaff and PatientServicesStaff roles are created idempotently at startup. Bootstrap only promotes an existing, email-confirmed, 2FA-enabled, non-locked-out account; if the configured account does not exist yet, startup logs a warning and waits for signup. Every startup with bootstrap enabled logs a warning, including when no email is configured. An existing account that has not completed these checks blocks bootstrap startup; disable bootstrap to finish setup.

All Admin endpoints require a staff role allowed for that section (see Staff Roles), currently confirmed email, enabled 2FA, a non-locked-out account and an `amr=mfa` sign-in claim. Password-only and remembered-browser sessions do not qualify. Anonymous requests receive the normal Identity login challenge with `ReturnUrl`; authenticated requests failing these rules receive HTTP 403. Admin writes require antiforgery tokens. Startup rejects anonymous Admin endpoints and Admin controllers without a registered staff policy.

## Staff Roles

| Role | Admin sections |
| --- | --- |
| Admin | Everything, including `/Admin/StaffAccess` |
| LabStaff | Dashboard, `/Admin/LabResults`, `/Admin/LabResultItems` |
| RadiologyStaff | Dashboard, `/Admin/RadiologyStudies` |
| PatientServicesStaff | Dashboard, `/Admin/RegistrationCodes` (PACD and clinic desks) |

To add staff, an Admin sends a **staff invitation** (see Staff Invitations below); the person signs up with it, confirms their email and enables two-factor authentication, and the invited role is granted automatically. For someone who already has a portal account (for example an employee who is also a patient), an Admin opens `/Admin/StaffAccess`, selects the account and grants LabStaff, RadiologyStaff or PatientServicesStaff. Admin itself is only granted through bootstrap. Grants and revocations take effect on the next request, refresh the account's security stamp and are written to the staff audit log. Staff see only their sections in the sidebar and dashboard; the dashboard hides the audit feed and import status from non-Admin staff. The portal's Administration link appears after the person signs in again. Staff choose patients through the existing patient context picker, which shows name and hospital number only.

## Admin Area

| Page | Purpose |
| --- | --- |
| `/Admin/Patients` | Hospital registry and separately confirmed portal links |
| `/Admin/RegistrationCodes` | Issue, print and revoke single-use hospital record codes for portal signup |
| `/Admin/Doctors` | Public doctor directory |
| `/Admin/PublicAdvisories` | Sanitized public advisories |
| `/Admin/ClinicalEncounters` | Dashboard and visit records |
| `/Admin/LabResults` | Laboratory records and protected PDF reports |
| `/Admin/LabResultItems` | Items within a selected lab (patients see them only when `PatientResults:ShowFullResults` is on) |
| `/Admin/RadiologyStudies` | Imaging studies, reports, release time and staff-only internal notes |
| `/Admin/StaffAccess` | Grant or revoke LabStaff, RadiologyStaff and PatientServicesStaff (Admin only) |
| `/Admin/StaffInvitations` | Invite staff by email and role, list invitations and revoke unused ones (Admin only) |
| `/Admin/Prescriptions` | Medications and refills |
| `/Admin/MedicationDoseSchedules` | Dose times within a selected prescription |
| `/Admin/PatientAllergies` | Patient allergies |
| `/Admin/Audit` | Read-only staff access and change history |
| `/Admin/Imports` | Templates, uploads, reconciliation and job status |

Lists use server-side search, fixed ordering and 25-row pages. Clinical forms show patient and parent context; edits cannot move records between patients or parents. Stale edits and deletes require review. Deletes with dependent records are blocked with counts. Hospital numbers retain leading zeros, are optional and unique ignoring case when present, and are never generated. Physician choices are stored as text snapshots.

### Account Linking and Audit

Hospital patient records exist independently of portal accounts. Staff verify identity, select one unambiguous email-confirmed account and confirm the check through a dedicated link form. Names, birth dates and similar emails are never automatic identity proof. Unlinking removes clinical access immediately; deleting an account preserves the hospital record and its clinical relationships. Patient ID documents remain account-owned.

Changes and staff audit entries commit together. Registry and clinical audit entries retain field names only, not earlier medical values; they cannot reconstruct prior records. Doctors and PublicAdvisories retain old/new values. Staff clinical views log affected patient references before display; failed audit persistence prevents display. The patient activity page remains unchanged. Both audit tables are append-only through application guards and have no edit/delete endpoints. Database access controls and retention procedures remain an operational responsibility.

General Identity administration, sensitive ID-document metadata, audit tables, migration history and import system metadata have no generic editor. Laboratory item values and result summaries are staff-only by default; when `PatientResults:ShowFullResults` is switched on, released results (and released radiology reports) are shown to the owning patient, and clinical and internal notes stay staff-only. Patient laboratory details provide protected PDF access instead of the previous claiming guide. New advisory saves sanitize HTML, but existing legacy HTML is not rewritten; review it before publication. The Content Security Policy requires scripts to be loaded from the app's own external JavaScript files.

## Hospital Record Codes

A hospital record code shows that a person has a record at DRMC and links that record to the portal account they create, without a separate staff linking step. The code is an enrollment credential for a verified hospital record, separate from a staff invitation.

1. At the PACD or a clinic desk (for example the Red Star Clinic), Patient services staff or an Admin check the patient's valid ID against the hospital record. They then open **Registration codes › Issue code**, or **Registration code** on the patient's details page.
2. The record must be unlinked and must have a birth date. Staff choose the issuing point, confirm the identity check, and print or show the slip. The slip has the code in the form `XXXXX-XXXXX-XXXXX` and a QR code. The full code is shown only once and is never stored: the database keeps a SHA-256 hash and the last five characters. Issuing a new code revokes any earlier active code for that record. Each issue and revoke is written to the staff audit log.
3. The QR opens `/Identity/Account/Register#code=…`. The code is in the URL fragment, which browsers do not send to the server. On the **Credentials** step, the **Hospital record code** field is filled in from the QR, typed by the patient, or scanned with the camera where the browser supports `BarcodeDetector`. Input is not case-sensitive and ignores spaces and hyphens.
4. Signup checks the code before creating the account. The code must be active (not used, revoked or expired), its record must still be unlinked, and the date of birth entered must match the record. If any check fails, signup shows one generic message and creates no account. On success, the code is marked used and the record is linked in a single serializable transaction, and `LINK_HOSPITAL_RECORD` is written to the patient activity log. Failures while accepting a code or invitation attempt account/link cleanup. A later audit or email-delivery failure can leave a created account and linked record; check state and resend confirmation instead of registering repeatedly.

A code is **required** to create a portal account: patients without a DRMC hospital record cannot get a code, so they cannot sign up. The signup page says so at the top, and a missing code is rejected on the server before any account is created. Setting `PatientRegistration:RequireHospitalRecordCode` to `false` makes the code optional again; accounts created that way can be linked by an Admin through **Verified portal link**. Staff use a staff invitation instead of a code, and the first Admin uses first-Admin setup (see First Admin).

## Staff Invitations

Staff usually have no hospital record, so they cannot get a hospital record code. An Admin invites them instead; patient signup stays strict.

1. An Admin opens **Staff invitations › Invite staff** (also linked from Staff access), enters the staff member's work email and picks LabStaff, RadiologyStaff or PatientServicesStaff. Admin is never granted by invitation. If an account with that email already exists, the Admin is sent to Staff access instead.
2. The portal emails a one-time link and shows it once with a QR code to print or show. The code has the same `XXXXX-XXXXX-XXXXX` format as hospital record codes but lives in its own table; only a SHA-256 hash and the last five characters are stored. A new invitation for the same email revokes the earlier unused one. Invitations expire after `StaffInvitations:LifetimeDays` (default 3). Invite and revoke actions are written to the staff audit log.
3. The link opens `/Identity/Account/Register#invite=…`. On **Credentials**, the **Staff invitation code** panel replaces the hospital record code panel. Staff can also switch to it with **DRMC staff with an invitation? Use it instead** and type the code.
4. Signup checks that the invitation is unused, not revoked, not expired and was sent to the email being registered. Any failure shows one generic message and creates no account. On success the invitation is marked accepted with a conditional update (a second signup with the same code fails), and `ACCEPT_STAFF_INVITATION` is written to the activity log.
5. The role is **not** granted at signup. It is granted automatically once the account has a confirmed email and two-factor authentication, right after the email is confirmed or 2FA is turned on, whichever comes last. The grant refreshes the security stamp and is written to the staff audit log under the inviting Admin. The person then signs out and signs in with an authenticator code to open Administration.

For local staff rehearsal, use a different email from Admin and the practice patient. Copy the invitation's signup URL into a separate browser profile, or sign the patient out of the private session first. Staff use the invitation instead of a hospital record code. Confirm email using the newest `[EMAIL OUTBOX]` link with `&amp;` replaced by `&`, then enable 2FA. No app restart or Admin bootstrap is needed to activate an invited role; sign out/in with an authenticator code after completing setup.

Admins can revoke an invitation while it is **Invited** or **Awaiting setup**; a revoked invitation never grants its role. Expiry limits accepting an unused invitation. An accepted invitation can remain Awaiting setup until confirmation and 2FA are completed, unless revoked. To grant access to someone who already has a confirmed portal account, use **Staff access** rather than creating another account.

## Admin Management Walkthrough

Use a separate development database and fictional people for these checks. Keep the Admin signed in in one browser profile and the patient in another. Private windows in the same browser usually share a session; use a different profile/browser for staff, or sign the patient out before testing staff. The short steps below describe the current forms and patient pages.

### 1. Add directory doctors and understand physician selection

Open **Doctors** (`/Admin/Doctors`) and create a fictional doctor with the required **Full name**, **Title** and **Department**. Select a department from the form's fixed catalog; there is no database Departments editor. Complete optional profile fields as needed and leave **Is active** enabled to show the doctor in `/Directory`. Use approved roster details for a real deployment.

Encounter, lab, radiology and prescription forms offer two physician sources:

| Choice | What staff enter |
| --- | --- |
| Directory | Select an existing doctor in **Directory physician** |
| Historical name | Type the physician documented on the record in **Historical physician name**; leave the directory choice Unassigned |

Only the selected source is used. The clinical record stores a name snapshot, so editing the directory later does not automatically rename earlier records. For practice without a directory doctor, use Historical name and a fictional name such as `Dr. Demo Physician`. Lab forms require both an ordering physician and pathologist; radiology forms require the requesting physician and reporting radiologist.

### 2. Create a hospital record and enroll its patient

1. In **Patients** (`/Admin/Patients`), create **Test Patient** with birth date **1990-01-01**. Hospital number is optional; leave it empty rather than inventing one. Saving creates a hospital record, not a login.
2. Open its **Details → Registration code**, or **Registration codes → Issue code** and select the record. Choose the issuing point, confirm the identity check for this fictional rehearsal, and issue the code. In a real workflow, staff first verify the patient's ID against the hospital record.
3. Copy or print the slip. The full code is shown once. A new code revokes the earlier active one; keep real slips private.
4. In the patient's separate session, open `/Identity/Account/Register`, enter the code on the Credentials step, use a different email from Admin, and enter the matching birth date on the Review step. Confirm the email using the same Development `[EMAIL OUTBOX]` process, then sign in.
5. Back in Admin, patient Details should show **Portal account: Linked**. This page does not display the linked email, and the top-right email belongs to the signed-in Admin. Patient signup does not rename the hospital record.

For a database learning check, run this read-only query in SSMS using the record's actual ID:

```sql
USE [DrmcPatientPortal_Dev]; -- Use your chosen database name.

SELECT p.Id, p.FullName, u.Email AS LinkedAccountEmail, u.EmailConfirmed
FROM dbo.PatientRecords AS p
LEFT JOIN dbo.AspNetUsers AS u ON u.Id = p.PortalUserId
WHERE p.Id = 1; -- Replace with the practice patient ID shown in Admin.
```

The email should be the patient account and `EmailConfirmed` should be `1`. Linking uses the valid code and matching birth date, not a name comparison. For real enrollment, record verified identity details. An already-linked record cannot receive a new enrollment code; existing accounts use the separately verified linking workflow.

### 3. Add a visit and check the patient view

In **Encounters** (`/Admin/ClinicalEncounters`), create a record for Test Patient. Enter a unique reference such as `DEMO-VISIT-001`, the encounter date/time in Manila, a department from the dropdown, the visit type and a physician. Optional complaints, summaries and instructions should be fictional for practice. Save, then open **Visits** in the linked patient's session and verify the new visit appears.

### 4. Link a lab to the visit and distinguish lab items

The encounter form does not create lab records. Open **Lab results** (`/Admin/LabResults`), select Test Patient and create one separately. Enter a unique accession number such as `DEMO-LAB-001`, a test name, category, collection time and both physician choices. In **Encounter**, select `DEMO-VISIT-001`; the dropdown lists only this patient's encounters.

For an available practice result, choose **Available** and set **Released at (Manila)** to a time that has passed and is not before collection. Save and refresh the patient's **Visits → visit details** and **Lab results**. The visit's lab section shows records linked through Encounter. Its empty wording, "No lab results were ordered," indicates no linked portal lab records; it does not establish what the hospital ordered.

| Admin section | What it stores |
| --- | --- |
| Lab results | Overall test record, accession number, patient/visit, availability, collection/release times, physicians and summary |
| Lab items | Measurements attached to that lab, including parameter, value, unit, reference range and flag |

For example, one CBC lab can contain hemoglobin, white blood cell and platelet items. Create the lab first, then select it under **Lab items** (`/Admin/LabResultItems`) to add measurements. Structured items and summaries are optional and remain supported. They are shown to patients only for released results when `PatientResults:ShowFullResults` is enabled. Clinical notes remain staff-only.

Attach a PDF of up to 10 MiB directly on the lab Create/Edit form, or manage it from record details. Leaving the upload blank preserves the current PDF. Patient lists show a green **Report available** action only when a PDF is attached, the status is Available, the release time has passed in Manila, and `PatientResults:ShowFullResults` is enabled. Each view/download requires the owning patient's current account password; no plaintext account password is stored in a lab record. The downloaded PDF itself does not have a file password.

Reports are encrypted in private storage configured by `LabReports:RootPath` (default `App_Data/LabReports`, outside `wwwroot`). Preserve and back up this directory with the database and Data Protection key ring. The existing `AddLabReportAttachments` migration creates `ReportFileName`, `ReportSize`, and `ReportUploadedAtUtc` on `LabResults`; fresh installations obtain these through the normal database migration command. The revised forms and patient pages require no further schema changes or column removals.

### 5. Add radiology and understand release status

In **Radiology** (`/Admin/RadiologyStudies`), create a fictional study for the patient with a unique accession number, study name, modality, body region, performed time and physician choices. Select the visit in **Encounter** if applicable. Save and inspect **Radiology** (`/Patient/Radiology`) in the patient's session.

**Final** and **Amended** both need a release time, findings and impression; Amended also needs an amendment note. Amended means the report has been revised. Patient availability is **Ready for claiming** only when the status is Final or Amended **and** the release time has passed in Manila. A future release still shows **Report in progress**. Performed time is separate, and release cannot precede the exam. With full results disabled, report sections remain concealed even when ready; the portal does not display imaging files.

### 6. Add prescriptions and dose schedules

In **Prescriptions** (`/Admin/Prescriptions`), create a fictional entry for Test Patient. Complete the Rx number, generic name, department, physician, prescribed/valid-until dates and refill counts. Set **Active** for the schedule rehearsal; remaining refills cannot exceed total refills. Use fictional dosage/instructions for testing and the clinician's documented prescription for actual records.

Open **Dose schedules** (`/Admin/MedicationDoseSchedules`), select that prescription, then add a dose time such as `08:00` and a display order such as `0`. In the patient's **Medications** (`/Patient/Medications`), refresh the daily medication schedule. It groups the saved doses by time and shows medication names and dosage. Only schedules belonging to Active prescriptions appear there.

### 7. Test staff permissions and explore the remaining tools

Follow [Staff Invitations](#staff-invitations) for a separate LabStaff account. Complete confirmation and 2FA, then sign out/in with an authenticator code. Confirm access to Lab results and Lab items and denial of Admin-only Staff access. Repeat with RadiologyStaff or PatientServicesStaff when rehearsing those workflows; use the role matrix above as the expected access.

Admin can also create **Allergies**, manage **Public advisories**, use **Imports**, and review **Audit**. Allergies belong to the selected hospital patient; advisory saves sanitize HTML. Imports require template review and reconciliation before approval. Audit is read-only. **Compact rows** in the top bar reduces table row spacing; click again for comfortable rows. The choice is remembered in that browser and is most visible in record lists.

Before client handover, repeat this workflow with two unrelated synthetic patients to verify each login sees only its linked records. Record the deployed commit, migration state, server/database, storage locations and responsible operators. Confirm institutional content, notifications and recovery in the actual hosting environment.

## Patient Radiology

`/Patient/Radiology` lists the signed-in patient's own imaging studies with imaging-type, date and search filters, a preparation guide and an overview of DRMC imaging services. A study is "Ready for claiming" once its status is Final or Amended and its release time (Manila time) has passed; otherwise it shows "Report in progress". Report sections and the plain-language summary are shown only for released studies and only when `PatientResults:ShowFullResults` is on. Another patient's study returns 404. Internal notes never reach patient pages. The portal shows no images; films and CDs are claimed from DRMC Radiology. Each detail view is recorded as `VIEW_RADIOLOGY_REPORT` in the patient activity log.

To try full results locally, set `$env:PatientResults__ShowFullResults = 'true'` before `dotnet run`. Do not commit it.

## CSV and Excel Imports

Download versioned CSV headers or the nine-sheet workbook from `/Admin/Imports`. CSV targets one `*_v1` template; `.xlsx` targets `Workbook_v1` and fixed named sheets. Preserve the supplied column order. Unknown columns, sheets, Identity fields and arbitrary mappings are rejected. See [Import Template Reference](#import-template-reference) below for columns and value formats.

Staff reconcile every source patient group to an existing hospital record or explicitly approve creating a new one. Source keys, names and birth dates are review aids, not automatic matching rules. Child rows also reference a source parent. Imports never link portal accounts, merge records, update existing rows or skip duplicates.

Upload, save mappings, queue a dry run, review errors and proposed counts, then queue approval. Dry run, approval and execution run on the durable worker; requests return to a polling status page. Dry runs write no clinical rows. Approval binds the file hash, mappings and validation version; changes require another dry run. Validation loads existing keys and relationships in bounded set-based queries, and execution checks them again within its transaction. The initiating staff account must remain eligible throughout.

### Import Template Reference

Download the running app's templates rather than rebuilding headers manually. Headers and worksheet names are case-sensitive, fixed and ordered. The current validation version is `1.1.0`; template names still end in `_v1`. No dedicated radiology-study import exists in this version; use its Admin form.

Patient and clinical templates start with `SourcePatientKey,SourcePatientName,SourceBirthDate`. A non-empty source patient key is required and limited to 200 characters. Names and birth dates are optional review hints, not automatic matching rules. The table's additional context follows those common columns, then the entity columns in the listed order:

| Worksheet | Additional context | Entity columns |
| --- | --- | --- |
| Patients_v1 | None | FullName, DateOfBirth, HospitalNumber |
| Doctors_v1 | No common patient context | FullName, Title, Department, SubSpecialty, ClinicRoom, ScheduleSummary, OffersTeleconsult, Biography, PrcLicenseMasked, IsActive |
| PublicAdvisories_v1 | No common patient context | Title, Slug, Category, Priority, Summary, ContentHtml, ImageUrl, IssuingUnit, PublishedAt, EffectiveUntil, IsPinned |
| ClinicalEncounters_v1 | SourceRecordKey | EncounterReference, EncounterDate, Department, Type, HistoricalDoctorName, ChiefComplaint, PrimaryDiagnosis, SecondaryDiagnosis, ClinicalSummary, CarePlanAndInstructions, VitalSignsRecorded, FollowUpDate, FollowUpNotes |
| LabResults_v1 | SourceRecordKey, SourceEncounterKey | AccessionNumber, TestName, Category, CollectedAt, ReleasedAt, Status, HistoricalDoctorName, Pathologist.HistoricalDoctorName, ResultSummary, PerformingUnit, ClinicalNotes |
| LabResultItems_v1 | SourceLabKey | ParameterName, Value, Unit, ReferenceRange, Flag |
| Prescriptions_v1 | SourceRecordKey | RxNumber, GenericName, BrandName, Dosage, DosageForm, Frequency, Instructions, Department, PrescribedAt, ValidUntil, LastRefillDate, RefillsTotal, RefillsRemaining, Status, HistoricalDoctorName |
| MedicationDoseSchedules_v1 | SourcePrescriptionKey | DoseTime, DisplayOrder |
| PatientAllergies_v1 | None | Allergen, Reaction, Severity, RecordedAt |

`SourceRecordKey` is a non-empty label unique within its parent template in the batch. Parent references use `batch:<SourceRecordKey>` for a row in the workbook or `existing:<hospital reference>` for an encounter reference, lab accession or Rx number. Prefixes are case-sensitive; hospital references are compared without case sensitivity. References are limited to 450 characters including the prefix. Missing, ambiguous or cross-patient parents block the batch; parent rows may follow children because validation orders dependencies. CSV targets one template and can reference existing parents. Workbook sheets can include batch parents and children.

Multiple groups may deliberately map to one existing hospital record; new groups are not merged automatically. Patient-template rows must explicitly create new records, not map to existing ones. Missing hospital numbers remain empty; duplicate numbers block creation. Children inherit the verified parent's patient ownership.

Value formats:

- Dates use `yyyy-MM-dd`; date/time uses `yyyy-MM-ddTHH:mm:ss` with up to seven fractional digits and no time-zone suffix. Clinical values are Manila wall time; advisory publication/effectivity is UTC.
- Dose time uses `HH:mm`, `HH:mm:ss` or fractional seconds accepted by the form. Excel date/time cells are converted to invariant formats. Hospital numbers and source identifiers must be text cells to preserve leading zeros.
- Booleans use `true` or `false` (empty is false); integers use base-10 whole numbers. Enums use exact declared names or defined numeric values. Departments must match the canonical catalog in `src/DrmcPatientPortal/Models/ClinicalDepartment.cs`. Lab status is Available, In progress or Pending Verification.
- CSV is UTF-8 (optional BOM), comma-delimited, with double-quoted fields and doubled inner quotes. Workbooks must contain values, not formulas. Do not add Identity, audit, ownership, rowversion or system-generated columns.
- Physician imports use explicit HistoricalDoctorName snapshots and the pathologist snapshot for labs; directory IDs are not import columns. Doctor Title is required. Advisory HTML uses the form's sanitizer and accepts HTTPS or safe relative links.

Duplicate references, slugs, hospital numbers and dose times block the batch. Imports also reject duplicate doctor name/department pairs, same-parent lab parameters and same-patient allergy names. These are duplicate checks, not identity matching or merge rules. Required values and field lengths follow the corresponding Admin form, including its 100,000-character cap where applicable.

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

## Server Hosting and Handover

The Windows walkthrough above uses Development and console notifications. A client server needs a separate approved database, trusted TLS, a dedicated application identity, persistent storage and real delivery settings. SQL Server Developer edition is for development/testing; the DBA selects the production edition, capacity, authentication and runtime/schema permissions.

For IIS, install IIS and the matching .NET 10 Hosting Bundle, then publish the reviewed source:

```powershell
dotnet publish src/DrmcPatientPortal -c Release -o ./output/publish
```

Configure the hosted app's environment and secrets through the app pool/deployment system, not a developer's temporary PowerShell window. Set Production, the real connection, `Notifications__Email__*` SMTP settings, `Notifications__Sms__*` HTTPS delivery settings, and absolute paths for the key ring, patient documents, temporary uploads and import staging. Grant the app identity the required filesystem access. An integrated SQL connection uses that runtime identity, not the interactive developer's account. Keep fixtures disabled and full-results disclosure at the approved setting.

Stop/drain old builds, have the designated migration operator apply the committed schema with the correct target and schema permissions, then start the new build. Create the first Admin with the same temporary bootstrap, confirmation and 2FA sequence; remove bootstrap from the hosted configuration after promotion. Console confirmation links are a Development aid; verify real email delivery on the server. Generate patient/staff slips through the real HTTPS portal origin so phones can reach them; validate host/scheme handling if an additional reverse proxy is used.

The deployment must keep the import worker running; configure IIS process lifetime/idle behavior for that requirement. Back up the SQL database, historical encryption keys and encrypted documents together and test recovery with the deployment identity. Replacing published code must preserve those data directories. Review [Key Protection and Recovery](#key-protection-and-recovery), the import limits, and client inputs below before handover.

## Known Limitations and Client Inputs

Representative source files and expected dataset volume are still needed. Fixed templates do not promise compatibility with arbitrary legacy spreadsheets. Batch limits are safety bounds, not performance guarantees; execution holds one serializable transaction for the whole batch and other writers may cause an atomic failure.

Confirm the meaning of existing clinical timestamps, approve audit/import/document retention and establish key backup/recovery ownership. Radiology needs to confirm the patient preparation guide, the films/CD and report releasing window, hours and claiming procedure, and whether the Basement 1 location and Local 208 shown on patient pages are current; the portal marks these as to be confirmed and shows no images. Approve whether patients may see full results before switching `PatientResults:ShowFullResults` on. The Filipino and Cebuano "Radiology" navigation labels need translator review. Departments, OPD and Malasakit guides, privacy/terms, official links and facility instructions remain hard-coded; decide whether they should become managed content. Clinical integrations, authoritative content and institutional deployment approval remain outside this application's current scope.
