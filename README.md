# DRMC Patient Portal

ASP.NET Core MVC and Identity application for Davao Regional Medical Center. Public pages provide hospital information, doctor listings and advisories. Patients can register, manage encrypted ID documents and view their linked hospital records, including the availability of laboratory results and radiology (imaging) studies. The staff Admin area manages patient records, clinical availability, public content and reconciled imports.

This application is not connected to hospital clinical, pharmacy, billing or social-work systems. Demonstration records, schedules and guidance require institutional approval before production use.

Start with [Local Setup](#local-setup), then [First Admin](#first-admin), [Staff Invitations](#staff-invitations) and the [Admin Management Walkthrough](#admin-management-walkthrough). Developers can also use [Testing](#testing), [Configuration](#configuration) and [Database Migrations](#database-migrations); deployment operators should review [Server Hosting and Handover](#server-hosting-and-handover) and [Key Protection and Recovery](#key-protection-and-recovery).

## Red Star clinic

Apply the full migration chain and run `redstar-protection upgrade` with the same database and key-ring settings before starting this build, including on an empty database (see [Local Setup](#local-setup)). The clinic's registry is under `/Admin/RedStar`; imports and exports are under `/Admin/RedStarImports`. Admin can invite and grant **Red Star staff**, but Admin alone cannot open either module. All clinic staff must confirm email, enable MFA and sign in with MFA. Registry reads/writes and source downloads have their own append-only access history.

1. Add a private clinic patient and program enrollment, with clinic code, medicines and verified status. A general hospital patient link never grants private clinic access.
2. Verify identity and ownership of an existing confirmed portal email before linking it from the clinic record. New links start with email reminders off. General registration codes use a neutral clinic-desk label.
3. Add staff-approved refill, laboratory or consultation dates. Only verified refill dates on active enrollments can generate reminders. Attendance confirmation records intent, not medicine collection.
4. Record a medicine pickup against its refill appointment, with actual medicines and optional days supplied. The estimated supply end is separate from the next approved visit. Continuing ART/PrEP dates can use the supply estimate as a suggestion; staff approve any repeat date. PEP/newborn courses never automatically create recurring refills.
5. Review patient date-change requests before approving a replacement. Closing enrollment, recording pickup, changing a date or unlinking cancels obsolete reminders and requests.

The patient page at `/Patient/RedStar` and Medications refill card appear only for staff-verified owners. Patients can confirm attendance, request a date change and opt into neutral email reminders to their confirmed account email. Clinic-only medicines and care records are not copied into shared prescriptions, visits or labs. Account deletion removes private portal bindings while retaining registry history.

Upload the original .xlsx through the clinic import screen, review included rows and explicit existing-record matches, then approve. Blank/formatted/note rows are skipped. Deferred clinical columns stay in the protected original archive. The name-only **RHIVDA 2025** sheet stays unverified; neither a test result nor treatment enrollment is inferred. Sources/previews are encrypted with separate Data Protection purposes. Back up the key ring with the database. Core exports contain literal spreadsheet strings, not formulas. Real registry files must never be fixtures, Git files or logs.

Core exports retain the staff workbook's eight tabs, complete headers and original column positions, including spacer columns. Saved values fill their matching staff columns; headers for clinical fields the portal does not store remain present with blank cells. Other saved portal fields follow the full staff column span so reimport preserves them. The source's name-only **RHIVDA 2025** list receives an explicit NAME header in the export. Transferred and deceased enrollments appear under **TRANS OUT** and **EXPIRED**, with their explicit program retained. Empty tabs keep their headers. Create and Edit enrollment forms offer all eight layouts for supported fields, defaulting to the saved program and status. Switching layouts changes headings and order while retaining entered values; program and treatment status remain separate staff decisions. The layout is retained when validation returns the form. Refill dates remain recorded through appointments and dispensing.

Both `RedStar:WorkerEnabled` and `RedStar:RemindersEnabled` default to **false**. After clinic verification and an SMTP rehearsal, enable both outside tracked configuration and restart. Generation runs at startup and daily at 8 AM Asia/Manila; reminder milestones are seven days before, the due date, seven days overdue, then calendar months from the original date. Catch-up creates the latest stage only. In-app reminders persist; email requires current opt-in and a confirmed account email. The queue rechecks ownership, date revision, status, consent and recipient before sending. Failures and delivery attempts are visible in the private Reminders screen. SMTP delivery is at-least-once after an interrupted send. Development's full-message console mail transport is never used for private reminders: external delivery is marked failed without logging recipients or bodies.

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
$env:LabReports__RootPath = Join-Path $localStorage 'LabReports'
$env:Imports__StagingPath = Join-Path $localStorage 'ImportStaging'

$target = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($env:ConnectionStrings__DefaultConnection)
Write-Host "Migration target: server=$($target.DataSource); database=$($target.InitialCatalog)"
if ($target.DataSource -ne 'localhost\SQLEXPRESS' -or $target.InitialCatalog -ne $databaseName) {
    throw 'Unexpected database target'
}

dotnet ef database update --project src/DrmcPatientPortal -- --environment Development
if ($LASTEXITCODE -ne 0) { throw 'Database migration failed; do not start the portal.' }

# Required even for an empty database. Stop every portal process first.
dotnet run --project src/DrmcPatientPortal --launch-profile http -- redstar-protection upgrade
if ($LASTEXITCODE -ne 0) { throw 'Private registry initialization failed; do not start the portal.' }
```

The explicit connection overrides Development user-secrets and tracked settings for this process. All clones under the same Windows user share the project's `UserSecretsId`; use per-terminal settings for independent installations. Environment variables use double underscores for nested settings. Store credentials outside Git; the connection above uses your Windows identity and needs no SQL password.

EF applies the committed migrations and creates the application tables. It can create a missing database itself when permitted, but the SSMS step lets you create and inspect it manually. Do not create another Initial migration or invent the portal tables. The initial migration does not transfer old SQLite data.

The second command initializes the registry's lookup key and encrypted verification record, then exits. It also verifies/applies the migration chain. Ordinary startup requires that initialized state; running only `dotnet ef database update` leaves a fresh database unable to start, with **Private registry protection is incomplete**. Use the same key directory for initialization and all subsequent runs. Existing installations must stop all replicas and preserve their original keys before this offline upgrade.

When EF reports `Done.` and the initialization command reports **Private registry protection upgrade verified and complete**, refresh **Tables** in SSMS and verify:

```sql
USE [DrmcPatientPortal_Dev]; -- Use your chosen database name.

SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
SELECT Ready FROM dbo.RedStarProtectionStates WHERE Id = 1;
SELECT COUNT(*) AS AccountCount FROM dbo.AspNetUsers;
SELECT COUNT(*) AS PatientRecordCount FROM dbo.PatientRecords;
```

This version has **15 migrations**, ending in `20261018121716_AddOutboundNotificationOutbox`, and the protection state above must have `Ready = 1`. A fresh installation has zero accounts and hospital patient records. Starting the app does not automatically apply migrations or seed demo data when fixtures are disabled.

### 5. Start the portal

Keep the same terminal open:

```powershell
dotnet run --project src/DrmcPatientPortal --launch-profile http
```

Open [http://localhost:5095](http://localhost:5095). The home page, `/Identity/Account/Login`, `/Identity/Account/Register`, `/Directory` and `/Advisories` should load. Empty directory and clinical pages are expected until staff add records. Stop the running app with **Ctrl+C** before changing environment variables; an already-running process does not pick up those changes.

`--project src/DrmcPatientPortal` selects the application from the repository root. `--launch-profile http` selects the Development/HTTP settings in `src/DrmcPatientPortal/Properties/launchSettings.json`. If your current folder is already `src/DrmcPatientPortal`, `dotnet run` uses that project and its default applicable launch profile. `.\run.ps1` is another launcher; it does not create the schema.

For local HTTPS, run `dotnet dev-certs https --trust`, then use `--launch-profile https` and open `https://localhost:7104`. A phone's `localhost` points to the phone, so QR rehearsal on another device needs a reachable test HTTPS host and correct link origin.

If startup reports **address already in use**, check for a portal still running from another terminal or worktree and stop it. For an independent rehearsal, keep the database/storage environment above and run `dotnet run --project src/DrmcPatientPortal --no-launch-profile --urls http://localhost:5096`, then open that port. Verify the new process actually starts before using browser responses as evidence of a successful setup.

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
# Keep private storage and keys outside the checkout.
export DRMC_LOCAL_STORAGE="$HOME/.local/share/drmc/DrmcPatientPortal_Dev"
export DataProtection__KeyRingPath="$DRMC_LOCAL_STORAGE/DataProtectionKeys"
export PatientDocuments__RootPath="$DRMC_LOCAL_STORAGE/PatientIdDocuments"
export PatientDocuments__TemporaryPath="$DRMC_LOCAL_STORAGE/TempUploads"
export LabReports__RootPath="$DRMC_LOCAL_STORAGE/LabReports"
export Imports__StagingPath="$DRMC_LOCAL_STORAGE/ImportStaging"

# Required collation; run once for a fresh database.
docker compose exec -T sqlserver bash -lc '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -b -Q "CREATE DATABASE [DrmcPatientPortal_Dev] COLLATE Latin1_General_100_BIN2;"' || exit 1
dotnet ef database update --project src/DrmcPatientPortal -- --environment Development || exit 1
dotnet run --project src/DrmcPatientPortal --launch-profile http -- redstar-protection upgrade || exit 1
dotnet run --project src/DrmcPatientPortal --launch-profile http
```

The container binds to `127.0.0.1` only and keeps data in the `drmc-sqlserver` volume; `docker compose down -v` deletes it. Use this password for local development only.

These commands run the application on the host and SQL Server in Docker. Linux hosts must complete [Linux OCR setup](#linux-ocr-setup) before running the initialization command or the app. The documented native OCR runtime is Ubuntu 24.04 x64; macOS native application execution is not verified. For the Linux application image, see [Container deployment](#container-deployment).

### Reset a disposable local installation

A fresh clone alone does not reset SQL Server or this project's shared user-secrets. To repeat Local Setup with no previous portal state:

1. Stop every portal process that uses the development database, including processes launched from other clones/worktrees. Confirm the server and database in your connection settings.
2. In SSMS, connect to `localhost\SQLEXPRESS`, select `master`, and delete only the disposable portal database. For the default development database:

   ```sql
   USE [master];
   IF DB_ID(N'DrmcPatientPortal_Dev') IS NOT NULL
   BEGIN
       ALTER DATABASE [DrmcPatientPortal_Dev] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
       DROP DATABASE [DrmcPatientPortal_Dev];
   END;
   ```

3. Remove that installation's private storage: `%LOCALAPPDATA%\DRMC\PatientPortal\DrmcPatientPortal_Dev` when using the setup above, or the exact paths configured for keys, ID documents, temporary uploads, lab reports and imports. Older installations may use `src/DrmcPatientPortal/App_Data` and `%LOCALAPPDATA%\DRMC\ImportStaging`. Inspect saved configuration before deleting it. Delete any disposable portal backups separately if they should also be erased; they are not removed by dropping SQL or deleting a checkout. Never delete a key ring while retaining data that still needs it.
4. From the old repository root, run `dotnet user-secrets clear --project src/DrmcPatientPortal` to remove this application's local overrides. All clones with the same `UserSecretsId` share them. Also remove old portal environment variables from the launching terminal, or open a fresh terminal; changing user-secrets does not clear environment variables.
5. Delete the old checkout after retaining any source changes you intend to keep, then clone into an empty destination. If it is the main checkout of linked Git worktrees, archive/remove those worktrees first; deleting its `.git` directory breaks their shared Git metadata. Clear the portal's browser site data so an old login/session is not reused.
6. Repeat Local Setup, including creating the database with the required collation, applying migrations and running the protection initialization command. Verify `Ready = 1` and zero accounts/patient records before creating the first Admin.

SQL Server Express, SSMS, .NET, the EF tool and the NuGet package cache can remain installed for this exercise. They do not contain the portal's application records. For Docker, `docker compose down -v` also destroys the configured SQL/key/private-storage volumes; use it only for a disposable Compose project.

### Configuration

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | SQL Server connection |
| `DataProtection__KeyRingPath` | Persistent key directory; default `App_Data/DataProtectionKeys` |
| `DataProtection__ProtectKeysWithDpapi` | Opt-in Windows current-user protection; default `false` |
| `DataProtection__CertificatePath` | Portable RSA private certificate (PFX) for key encryption; required outside Development unless using Windows DPAPI |
| `DataProtection__CertificatePasswordFile` | Optional password-secret file; trailing line endings are removed; omit for a passwordless PFX |
| `DataProtection__DecryptionCertificates__0__Path` / `__PasswordFile` | Historical private certificates retained during rotation; increase the numeric index for additional certificates |
| `PatientDocuments__RootPath` | Encrypted patient ID document directory |
| `PatientDocuments__TemporaryPath` | Temporary encrypted registration uploads |
| `PatientDocuments__MaximumFileSizeMb` | ID photo upload limit; default and hard ceiling 5 MB per photo; configuration can lower it |
| `PatientDocuments__TemporaryFileLifetimeMinutes` | Temporary upload lifetime; default 30 minutes |
| `Imports__StagingPath` | Encrypted import staging; default the current user's local application-data DRMC/ImportStaging directory |
| `Notifications__Email__*` | SMTP host, port, TLS, credentials and sender |
| `Notifications__Sms__*` | HTTPS webhook, API key and sender ID |
| `PatientResults__ShowFullResults` | Show released laboratory values and radiology reports to the owning patient; default `false`. Keep it `false` in tracked settings until DRMC approves full results in the portal |
| `PatientRegistration__RequireHospitalRecordCode` | Require a hospital record code at signup; default `true`, so only patients with a DRMC hospital record can create an account. Set `false` only for a supervised rollout where staff link accounts manually |
| `PatientRegistration__CodeLifetimeDays` | How long a hospital record code stays valid; default `7`, limited to 1–30 days |
| `StaffInvitations__LifetimeDays` | How long an unused staff invitation stays valid; default `3`, limited to 1–14 days |
| `Identity__RequireConfirmedAccount` | Require a confirmed email before sign-in; default `true`. In Development the confirmation link is written to the console |
| `Portal__PublicOrigin` | Required HTTPS origin in Production, such as `https://portal.example.org`; select the real domain before deployment. All account, staff invitation and hospital registration links use this origin |
| `AllowedHosts` | Explicit semicolon-separated hostnames including the public origin host; Production rejects wildcards. Tracked defaults permit local development only |
| `Proxy__KnownProxies__0` (and subsequent indexes) | IP addresses of trusted immediate reverse proxies. Only their forwarded client address and scheme are honored; forwarded hostnames are ignored |
| `LabReports__RootPath` | Private encrypted PDF directory; default `App_Data/LabReports`; fixed PDF limit 10 MB |
| `Ocr__NativeLibraryPath` | Native OCR library root containing an `x64` subdirectory; Windows normally uses the bundled native DLLs |
| `Ocr__TessDataPath` | Directory containing `eng.traineddata`; default published `tessdata` or content-root `tessdata` |
| `AccountPrivacy__MinimumResponseMilliseconds` | Minimum failed sign-in/reset/resend duration; default 250 ms, supported range 250–10000 |
| `AccountEmail__MaximumAttempts` | Delivery attempts, default 5 (1–10) |
| `AccountEmail__LifetimeMinutes` | Queued message expiry, default 60 (1–1440) |
| `AccountEmail__RetentionDays` | Completed delivery metadata retention, default 7 (1–30) |
| `AccountEmail__WorkerEnabled` | Default `true`; disable only in tests or when another new-build process runs the worker |
| `Notifications__Outbox__WorkerEnabled` | Default `true`; independently dispatches staff invitations and queued email/SMS |
| `Notifications__Outbox__MaximumAttempts` | Delivery attempts, default 5 (1–10) |
| `Notifications__Outbox__LifetimeMinutes` | Generic message expiry, default 60 (1–1440); staff messages expire with their invitation |
| `Notifications__Outbox__RetentionDays` | Completed delivery metadata retention, default 7 (1–30) |
| `RedStar__WorkerEnabled` / `RedStar__RemindersEnabled` | Both default `false`; enable after clinic verification and delivery rehearsal. The worker creates private in-app reminders and sends opted-in email |
| `RedStar__MaximumAttempts` | Email delivery attempts, default 5 (1–10); failures are visible only to Red Star staff |
| `RateLimits__AuthenticationPerIp` / `AuthenticationPerAccount` | Sign-in/verification limits per minute; defaults 60/IP and 10/account-plus-IP |
| `RateLimits__EmailPerIp` / `EmailPerAccount` | Reset/resend limits per 15 minutes; defaults 20/IP and 3/account-plus-IP |
| `RateLimits__ScansPerIp` / `ConcurrentScans` | Defaults 5 scans/minute/IP and 2 simultaneous scans/process |
| `RateLimits__PdfPerUser` | PDF password verification requests per minute; default 10/user |

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
8. `20261007153732_AddLabReportAttachments`: adds encrypted PDF attachment metadata to laboratory results.
9. `20261007160128_AddPrescriptionAdministrationRoute`: adds the optional prescription administration route.
10. `20261011093226_AddPortalSecurityState`: adds revocable authentication sessions, unique authenticator-code consumption records and an encrypted account-email queue. Repairs orphan invitation acceptance references, then adds an accepting-account foreign key with `ON DELETE SET NULL`; invitation history and acceptance timestamps remain intact.
11. `20261011120435_AddPrivateRedStarRegistry`: adds private clinic patients, enrollments, care/refill appointments, dispensing history, reschedule requests, reminder deliveries, encrypted import archives and append-only audit records. Shared hospital links confer no clinic access. Account deletion detaches private account bindings while preserving registry history. Downgrade is blocked while registry/import/audit records exist.
12. `20261011132407_AddWebsiteUsageReporting`: adds daily aggregate traffic/adoption counts and temporary deduplication markers. Analytics collection defaults off; cleanup removes expired daily identifiers/keys and retains aggregates for 13 months. Downgrade removes usage reporting history.
13. `20261018065159_EncryptPrivateRedStarRegistry`: adds protected registry fields, keyed lookup indexes and the protection-state record. After schema migration, stop all portal processes and run `redstar-protection upgrade` with the original key ring to initialize or convert and verify the registry before startup.
14. `20261018121716_AddOutboundNotificationOutbox`: adds encrypted staff/general email and SMS deliveries, lease/schedule indexes, SQL row versions and one email per staff invitation. Existing account and clinical queues remain intact. Apply before starting this build; reversing it deletes queued messages and delivery metadata.

The initial SQL Server migration is `20260929123803_InitialSqlServer`. During upgrade, drain and stop older builds, apply migrations, then run only the new build. Older workers cannot respect the lease protocol.

The initial migration's read-only collation check participates in its schema transaction. An interrupted fresh installation rolls back and can be migrated again. For an installation partially initialized by an older build, stop the app, back up and inspect both schema and `__EFMigrationsHistory`. Restore a known complete backup, or have the migration operator reconcile the partial schema against the initial migration before retrying. For a verified empty, disposable installation, the operator may recreate the database. The app never automatically drops partial tables or patient data.

### Linux OCR setup

Supported Linux runtime: Ubuntu 24.04 x64. Run the idempotent setup script before starting the app:

```bash
sudo bash scripts/setup-ocr-ubuntu.sh /opt/drmc/ocr
export Ocr__NativeLibraryPath=/opt/drmc/ocr
export Ocr__TessDataPath=/usr/share/tesseract-ocr/5/tessdata
export LD_LIBRARY_PATH=/opt/drmc/ocr/x64
```

For systemd, place those three values in the service's environment file. The script installs distro-managed Tesseract/Leptonica and English language data and creates app-local wrapper-compatible names under `x64`; see the [wrapper's native library loader](https://github.com/charlesw/tesseract/blob/master/src/Tesseract/Internal/InteropDotNet/LibraryLoader.cs). Do not overwrite system libraries. The application initializes the engine and processes synthetic JPEG and PNG probes before accepting requests. Missing libraries or language data stop startup with actionable diagnostics. Runtime dependency failures return 503; malformed images return 400. A valid image with unreadable text still permits manual entry.

Registration ID scanning runs locally in the bounded OCR worker. Selecting a photo confirms only that a file is selected. Scanning first reads the original image, then retries missing details using resized grayscale images, adaptive thresholds, text columns and field regions. Classic PhilSys regions require matching printed-label positions; other layouts use detected labels and word coordinates. Retries share the existing ten-second execution limit for both photos. Startup probes perform a single recognition pass.

The review form lists recovered details and expected fields that still need entry, using the selected ID's capability metadata. All entries remain editable. Unknown blood type stays blank, and dates or names that cannot be read reliably require manual entry. Every rescan replaces the previous scan's fields, including empty values. OCR assists review and does not establish document authenticity. Automated image fixtures contain synthetic identities only; personal ID images must not be committed as fixtures or written to diagnostic logs.

### Authentication and request controls

Authentication cookies contain an opaque session identifier. Every authenticated request performs an indexed SQL session/user-stamp check. Sign-out commits revocation of that browser's session before success, including copied-cookie replay; other browsers remain signed in. Password changes rotate the initiating browser's session and invalidate other sessions on their next request. Password resets invalidate all older sessions. Sliding renewal updates an existing active session and cannot recreate a revoked session. Existing cookies from older builds require a fresh sign-in. Verified MFA survives principal refresh; authenticator sign-in codes are consumed atomically, including Identity's two adjacent time steps in each direction. Enrollment verification remains separate.

Unknown, incorrect-password, locked and unconfirmed sign-ins show the same failure message and perform password-hash work. Failed sign-ins, reset requests and confirmation resends wait at least the configured duration. Account emails are encrypted with the persistent Data Protection key ring and queued in SQL, so public requests do not wait for SMTP. Workers claim atomic two-minute leases, use a 30-second transport deadline and bounded backoff, and recover abandoned leases. SMTP delivery is at least once: an interrupted acknowledgement can cause a duplicate email. Delivered, failed and expired messages have their encrypted payload cleared; metadata is retained for seven days by default. Failure logs exclude message bodies and tokens. Preserve the shared key ring for overlapping workers and restarts.

Registration commits its account, record/invitation claims, document metadata, security audit and verification email in one transaction. A confirmed rollback cleans newly stored ID photos; an uncertain commit is checked by the stable message ID before files are removed. If verification is unavailable, signup is not replayed and photos are retained for recovery.

Staff invitations commit their protected delivery copy with the invitation and audit, then show a queued receipt. Their independent outbox worker polls at startup and every two seconds. Replaced, revoked, accepted and expired invitations cannot be sent when detected before transport. Account, staff/SMS and clinical delivery retry after 30 seconds, two minutes, eight minutes and then 30 minutes, capped by their attempt limits and expiry. Exhausted generic messages become `DeadLetter`; protected payloads and leases are cleared on terminal outcomes. Terminal metadata is retained for seven days by default and cleanup runs every five minutes. Provider error text is never persisted or logged. SMS has a durable queue API but no automatic patient-message producers. Console email/SMS remain Development transports; private clinical email retains its console suppression.

For application containers, mount `DataProtection__KeyRingPath` on persistent storage shared by overlapping replicas and retain the SQL database across replacement. Losing the key directory makes encrypted pending messages unreadable even when the database survives. The optional Compose `portal` profile mounts persistent key and private-storage volumes; see [Container deployment](#container-deployment).

Limits return HTTP 429 and `Retry-After` immediately with no rejected-request queue. Anonymous account keys use the normalized submitted email without testing account existence. Limit counters and scan concurrency are process-local: overlapping recycling temporarily increases aggregate capacity. Configure limits appropriately for the deployment and monitor 429 rates. Forged forwarding headers cannot change client identity unless sent by a configured proxy. Form text containing a decoded NUL is rejected with HTTP 400; uploaded file bytes are not inspected for NULs. PDF password checks use a separate user throttle and neither increase nor reset Identity's sign-in failure counter.

Patient visits, laboratory results, radiology, prescriptions, ID wallet, directory and advisories use fixed 25-row pages, SQL totals and stable ordering. Medication schedules and allergies have independent pages. The dashboard loads only the latest visit/laboratory preview and separate SQL aggregate counts. SQL retries are opt-in for standalone reads and operations with stable idempotency keys: three retries, maximum two-second delay. Business transactions, audit writes and import execution are never replayed after an uncertain commit; existing deadlock rollback handling remains in place. See [EF Core connection resiliency](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency).

### Trusted hosting and security rollout

Before deployment, choose the real public domain, set `Portal__PublicOrigin` to its HTTPS origin, and set `AllowedHosts` to explicit hostnames including that name. Production startup rejects missing origins, HTTP origins and wildcard host configuration. IIS HTTPS bindings must use the same hostname and certificate; remove broad default bindings that route unrelated hosts to this app. For out-of-process IIS hosting, explicitly configure the immediate ANCM loopback address (`127.0.0.1` or `::1`, as used by that host) in `Proxy__KnownProxies`. With an additional reverse proxy, configure only its immediate IP addresses, prevent direct public access to the upstream, and preserve the configured host. Without configured proxies, forwarded headers are ignored even when a host enables forwarding globally. See [Microsoft host filtering guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/host-filtering?view=aspnetcore-10.0).

Drain older builds, back up SQL, encrypted uploads/PDFs and the key ring, configure the origin/hosts, key protection and OCR runtime, apply the full migration chain and run `redstar-protection upgrade` offline, then start only the new build. Require existing users to sign in again. Verify reset, confirmation, email-change and invitation links from the actual host. Monitor authentication-query latency, rate-limit rejections, pending/failed email deliveries and retry exhaustion. The Ubuntu 24.04 CI job installs OCR dependencies, boots the app against SQL Server and runs the regression suite. Passing Windows tests alone does not establish Linux readiness.

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

The shared test host disables background security cleanup so tests that advance a simulated clock cannot remove another test's sessions. Dedicated cleanup tests drive that worker explicitly; normal application startup keeps the worker enabled.

Synthetic OCR cards use the bundled OFL-licensed Arimo font and the pinned `Tesseract.Data.English` model so their pixels and language data remain consistent on Windows and Linux. They do not depend on host font discovery or the distro's model variant. Application startup tests separately exercise the configured native OCR runtime and language data. CI checks these image fixtures before running the remaining integration tests, then verifies container persistence.

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
2. The record must be unlinked and must have a birth date. Staff choose the issuing point, confirm the identity check, and print or show the slip. The slip has the code in the form `XXXXX-XXXXX-XXXXX-XXXXX` and a QR code. The full code is shown only once and is never stored: the database keeps a SHA-256 hash and the last five characters. Issuing a new code revokes any earlier active code for that record. Each issue and revoke is written to the staff audit log.
3. The QR opens `/Identity/Account/Register#code=…`. The code is in the URL fragment, which browsers do not send to the server. On the **Credentials** step, the **Hospital record code** field is filled in from the QR link, typed by the patient, or scanned with **Scan QR**. Camera scanning uses native detection when available and a bundled jsQR fallback otherwise; frames stay in the browser. Camera access requires HTTPS or localhost and permission. Input is not case-sensitive, ignores spaces and hyphens, and still accepts previously issued 15-character codes until expiry.
4. Signup checks the code before creating the account. The code must be active (not used, revoked or expired), its record must still be unlinked, and the date of birth and structured name entered must match the record. First and last names must match; middle name and suffix must also match when present on the record. Comparison ignores case, accents, whitespace differences, periods and commas. If any check fails, signup shows one generic message and creates no account. A name mismatch leaves the code unchanged. On success, the code is marked used and the record is linked in a single serializable transaction, and `LINK_HOSPITAL_RECORD` is written to the patient activity log. Failures while accepting a code or invitation attempt account/link cleanup. A later audit or email-delivery failure can leave a created account and linked record; check state and resend confirmation instead of registering repeatedly.

A code is **required** to create a portal account: patients without a DRMC hospital record cannot get a code, so they cannot sign up. The signup page says so at the top, and a missing code is rejected on the server before any account is created. Setting `PatientRegistration:RequireHospitalRecordCode` to `false` makes the code optional again; accounts created that way can be linked by an Admin through **Verified portal link**. Staff use a staff invitation instead of a code, and the first Admin uses first-Admin setup (see First Admin).

## Staff Invitations

Staff usually have no hospital record, so they cannot get a hospital record code. An Admin invites them instead; patient signup stays strict.

1. An Admin opens **Staff invitations › Invite staff** (also linked from Staff access), enters the staff member's work email and picks LabStaff, RadiologyStaff or PatientServicesStaff. Admin is never granted by invitation. If an account with that email already exists, the Admin is sent to Staff access instead.
2. The portal emails a one-time link and shows it once with a QR code to print or show. The code has the same `XXXXX-XXXXX-XXXXX-XXXXX` format as hospital record codes but lives in its own table; only a SHA-256 hash and the last five characters are stored. A new invitation for the same email revokes the earlier unused one. Invitations expire after `StaffInvitations:LifetimeDays` (default 3). Invite and revoke actions are written to the staff audit log.
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

1. In **Patients** (`/Admin/Patients`), create a record with last name **Patient**, first name **Test**, and birth date **1990-01-01**. Middle name, suffix, and hospital number are optional. Saving creates a hospital record, not a login. Names display as **Patient, Test**. Existing records receive blank name parts during migration and require staff to enter them before registration can succeed; legacy full names are not split automatically.
2. Open its **Details → Registration code**, or **Registration codes → Issue code** and select the record. Choose the issuing point, confirm the identity check for this fictional rehearsal, and issue the code. In a real workflow, staff first verify the patient's ID against the hospital record.
3. Copy or print the slip. The full code is shown once. A new code revokes the earlier active one; keep real slips private.
4. In the patient's separate session, open `/Identity/Account/Register`, enter the code on the Credentials step, use a different email from Admin, and enter the matching name parts and birth date on the Review step. Confirm the email using the same Development `[EMAIL OUTBOX]` process, then sign in.
5. Back in Admin, patient Details should show **Portal account: Linked**. This page does not display the linked email, and the top-right email belongs to the signed-in Admin. Patient signup does not rename the hospital record.

For a database learning check, run this read-only query in SSMS using the record's actual ID:

```sql
USE [DrmcPatientPortal_Dev]; -- Use your chosen database name.

SELECT p.Id, p.FullName, u.Email AS LinkedAccountEmail, u.EmailConfirmed
FROM dbo.PatientRecords AS p
LEFT JOIN dbo.AspNetUsers AS u ON u.Id = p.PortalUserId
WHERE p.Id = 1; -- Replace with the practice patient ID shown in Admin.
```

The email should be the patient account and `EmailConfirmed` should be `1`. Signup linking requires the valid code, matching name parts, and matching birth date. For real enrollment, record verified identity details. An already-linked record cannot receive a new enrollment code; existing accounts use the separately verified linking workflow.

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

Reports are encrypted in private storage configured by `LabReports:RootPath` (default `App_Data/LabReports`, outside `wwwroot`). Preserve and back up this directory with the database and Data Protection key ring. `AddLabReportAttachments` creates `ReportFileName`, `ReportSize`, and `ReportUploadedAtUtc` on `LabResults`. Apply the full migration chain through the normal database migration command.

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

Download the running app's templates rather than rebuilding headers manually. Headers and worksheet names are case-sensitive, fixed and ordered. The current validation version is `1.2.0`; template names still end in `_v1`. Older patient templates containing `FullName` must be replaced with the separate name columns below; stale approvals require validation again. No dedicated radiology-study import exists in this version; use its Admin form.

Patient and clinical templates start with `SourcePatientKey,SourcePatientName,SourceBirthDate`. A non-empty source patient key is required and limited to 200 characters. Names and birth dates are optional review hints, not automatic matching rules. The table's additional context follows those common columns, then the entity columns in the listed order:

| Worksheet | Additional context | Entity columns |
| --- | --- | --- |
| Patients_v1 | None | LastName, FirstName, MiddleName, Suffix, DateOfBirth, HospitalNumber |
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

## Website usage reporting

Admin accounts with confirmed email and MFA can open `/Admin/WebsiteUsage` or use the two Today cards on the dashboard. Day, month and custom-range filters use Asia/Manila dates; custom ranges are limited to 366 days. `/Admin/WebsiteUsage/Export` downloads the same filtered daily counts as CSV. Anonymous visitors and other staff roles cannot open the report or export.

`WebsiteUsage:Enabled` defaults to **false**. Apply `AddWebsiteUsageReporting` before deploying this build, rehearse with synthetic traffic in staging, and confirm the declared reporting purpose and production processing basis with DRMC's DPO before setting `WebsiteUsage__Enabled=true` outside tracked configuration. Restart after changing the setting. Disabling collection preserves existing reports and keeps cleanup active. Startup does not automatically migrate the database.

Estimated visitors use a cookieless daily HMAC of the trusted-proxy-resolved IP address and User-Agent. Active patients use a separate daily HMAC of the signed-in non-staff account ID, deduplicating devices. The two audiences overlap. Visitor-days and patient-days are sums of daily counts, **not unique monthly people**. No raw addresses, browser strings, account IDs, page URLs, query strings or clinical information are stored by analytics. Shared networks, changing addresses/browsers, undetected automation and anonymous staff affect visitor estimates. Successful GET HTML pages count; Admin, Identity, files, APIs, errors, recognized bots/monitors, prefetch and DNT/GPC opt-outs do not.

Daily keys are encrypted using a separate Data Protection purpose and shared through SQL Server. Keep the existing persistent key ring shared across overlapping app processes. SQL uniqueness and a transaction protect marker/counter updates across processes and restarts. Collection has a 500 ms deadline, an isolated context and sanitized failure logs; it never changes the page response on failure. Maintenance runs at startup and every minute, even with collection disabled. Enable an always-running application host for complete zero-traffic coverage (for IIS, configure AlwaysRunning/preload and avoid idle shutdown).

Temporary identifiers and expired daily keys are purged after the next Manila midnight plus five minutes; aggregates are retained for 13 calendar months. Cleanup catches up after downtime. Database backups remain subject to the institution's existing backup and retention policy. Account deletion does not alter anonymous daily totals, since analytics has no persistent account binding. Today is partial; interrupted collection or a heartbeat gap exceeding two minutes is partial. Unmeasured/expired dates show Not collected. Averages use complete measured days, including true zero-traffic days. No historical backfill is attempted. Monitor sanitized `Website usage collection failed` / `Website usage maintenance failed` warnings and the report's coverage; storage outages can undercount observed traffic.

## Time-Zone Rules

Staff-entered clinical dates/times are Manila wall time stored as `datetime2` without an offset. Patient date filters use explicit Manila today. Advisory publication/effectivity dates and system, audit and import timestamps are UTC. Browser date/time fields preserve additional stored fractional precision when unchanged. Existing clinical timestamps are not shifted; their original time-zone meaning still needs confirmation before any conversion.

## Key Protection and Recovery

Outside Development, startup requires key encryption: configure `DataProtection__CertificatePath` and optionally `DataProtection__CertificatePasswordFile`, or use Windows current-user DPAPI. The certificate must be a PFX containing a usable RSA private key. Relative certificate/password paths resolve against the application content root. A password file contains only the password (an optional final newline is accepted). Store these secrets outside Git, the image, the web root and the key-ring volume; grant only the application identity read access. Development can use plaintext keys only when the ring contains no protected keys. Conflicting protection settings, inaccessible storage, malformed keys, or unreadable historical keys fail startup before database upgrades or requests; there is no plaintext or ephemeral fallback.

Before enabling encryption on an existing installation, stop/drain **all** older replicas and back up the database, complete key directory and private storage using protected backups. Start one upgraded process with the chosen protection settings. Under a cross-process file lock, startup preflights existing envelopes, encrypts plaintext secrets, verifies each round-trip, and atomically replaces each converted key file. Key IDs and key material remain the same; no plaintext backup is created. An interrupted conversion is safe to resume with the same settings. Verify existing document/import reads and account sign-in before starting additional upgraded replicas. Never remove old key XML to make startup succeed.

Windows installations may retain `DataProtection__ProtectKeysWithDpapi=true` under the original account and machine. To migrate a DPAPI ring to containers, stop all portal processes, keep the original Windows identity/profile, configure the private certificate and set `ProtectKeysWithDpapi=false`, then run the upgraded process once under that original identity to convert the ring. Verify reads before copying the full directory to the container volume. Linux rejects DPAPI envelopes; it cannot recover them. Setting both DPAPI and certificate protection is an error.

For certificate rotation, distribute the new certificate **and all historical decryption certificates** to every replica before any replica writes keys with the new certificate. Configure the new `CertificatePath`/`CertificatePasswordFile` and add each previous certificate under `DataProtection__DecryptionCertificates__0__Path` and `__PasswordFile` (then index 1, 2, ...). Historical certificates can also be configured on replicas still writing with the old certificate during the transition. For Compose, mount historical certificate/password files read-only through a deployment override and configure their container paths. Retain all historical private certificates and key XML for as long as encrypted data/backups need them. Key expiry or certificate rotation does not make old documents expendable.

Back up SQL, the entire key directory (including the private-registry blind-index key), patient documents, laboratory reports and import staging as one recoverable deployment. Back up private certificates/passwords separately with restricted access. Rehearse restoration to an isolated deployment with the same `DRMC.PatientPortal` application name, shared original keys and certificate set; verify old documents, queued payloads and the registry canary before directing traffic there. Losing historical keys or their private certificates can make those records permanently unreadable. Do not reset unreadable jobs to bypass decryption failures. Encryption does not protect against a compromised application identity or database/storage administrator.

TOTP consumption and cookie session validity are SQL-backed. A unique `(UserId, KeyFingerprint, TimeStep)` insert enforces single use across replicas and token purposes. Database failures never authenticate through a local cache. Cookie revocation, expiry and security-stamp changes are checked against SQL on every request. An independent worker runs security cleanup immediately and every five minutes, deleting expired records in batches of 100 even when email delivery is disabled or failing. TOTP records remain until the end of the accepted ±2-step window. Keep all replica clocks synchronized to UTC.

### Container deployment

The default `docker compose up -d` still starts only the development SQL Server. The optional `portal` profile builds an Ubuntu 24.04 AMD64 image with .NET 10 and the existing native OCR dependencies. SQL Server Developer edition in this manifest is for development/testing; production requires the approved SQL edition and credentials.

1. Provision a dedicated RSA private certificate as `secrets/keyring.pfx` and its password as `secrets/keyring.password` (an empty file for a passwordless PFX). These paths are ignored by Git and excluded from the build context. Override their locations with `DRMC_KEYRING_CERTIFICATE_FILE` and `DRMC_KEYRING_PASSWORD_FILE`. Local Compose file secrets retain host ownership: on Linux, grant container UID/GID `1654:1654` read access, for example `sudo chown 1654:1654 secrets/keyring.pfx secrets/keyring.password` and `sudo chmod 0400 secrets/keyring.pfx secrets/keyring.password`. Use restrictive host directory permissions and equivalent ACLs on Windows.
2. Create ignored `.env.portal` with `ConnectionStrings__DefaultConnection` (SQL host `sqlserver` for this Compose database), `Portal__PublicOrigin`, explicit `AllowedHosts`, and the existing `Notifications__Email__*` and `Notifications__Sms__*` production settings. Use a dedicated database/runtime identity for production. `DRMC_PORTAL_ENV_FILE` can select another deployment-managed file. Keep development fixtures/bootstrap disabled unless performing the documented initial provisioning.
3. Set `MSSQL_SA_PASSWORD`, run `docker compose --profile portal build`, then `docker compose up -d --wait sqlserver`. With all existing replicas stopped and the migration identity configured, run `docker compose --profile portal run --rm portal redstar-protection upgrade`. This applies the existing migration chain and initializes/verifies registry protection; ordinary replicas do not run migrations.
4. Start with `docker compose --profile portal up -d --scale portal=2 portal`. Each replica runs as non-root UID 1654 with a dynamically assigned loopback HTTP port (`docker compose port --index 1 portal 8080`). Put the approved HTTPS reverse proxy in front and configure its trusted IPs through `Proxy__KnownProxies`. The certificate and password are mounted read-only. Fresh named-volume directories inherit the image's restricted ownership; repair existing ownership offline rather than starting the application as root.
5. Preserve `drmc-sqlserver`, `drmc-keyring` and `drmc-private-storage` across replacement. The key ring is `/var/lib/drmc/keys`; private storage has distinct patient-document, temporary-upload, lab-report and import directories under `/var/lib/drmc/storage`. **`docker compose down -v` deletes these volumes.** On separate Docker hosts, provision a common read/write filesystem and mount the same key and private-storage directories through a deployment override/shared volume driver; ordinary local named volumes are scoped to one Docker host. The filesystem must support exclusive file locks and atomic same-directory replacement. Replicas also require the same SQL database, certificate set and application name. Registration upload binding currently uses process-local ASP.NET Session; configure routing affinity or a shared session provider for that workflow when distributing registration requests across replicas.

Rehearse container recreation and storage recovery in a disposable deployment before using this configuration for persistent data. Docker/Linux execution is separate from the Windows local-setup check.

## Server Hosting and Handover

The Windows walkthrough above uses Development and console notifications. A client server needs a separate approved database, trusted TLS, a dedicated application identity, persistent storage and real delivery settings. SQL Server Developer edition is for development/testing; the DBA selects the production edition, capacity, authentication and runtime/schema permissions.

For IIS, install IIS and the matching .NET 10 Hosting Bundle, then publish the reviewed source:

```powershell
dotnet publish src/DrmcPatientPortal -c Release -o ./output/publish
```

Configure the hosted app's environment and secrets through the app pool/deployment system, not a developer's temporary PowerShell window. Set Production, the real connection, `Notifications__Email__*` SMTP settings, `Notifications__Sms__*` HTTPS delivery settings, and absolute paths for the key ring, patient documents, temporary uploads and import staging. Grant the app identity the required filesystem access. An integrated SQL connection uses that runtime identity, not the interactive developer's account. Keep fixtures disabled and full-results disclosure at the approved setting.

Stop/drain old builds, have the designated migration operator apply the committed schema with the correct target and schema permissions, then run the published application once with `redstar-protection upgrade` using the deployment's database, original persistent key ring and key-protection settings. For a framework-dependent publish, change to the publish directory and run `dotnet DrmcPatientPortal.dll redstar-protection upgrade` with the deployment environment configured. Start the new build only after the command succeeds. Create the first Admin with the same temporary bootstrap, confirmation and 2FA sequence; remove bootstrap from the hosted configuration after promotion. Console confirmation links are a Development aid; verify real email delivery on the server. Generate patient/staff slips through the real HTTPS portal origin so phones can reach them; validate host/scheme handling if an additional reverse proxy is used.

The deployment must keep the import worker running; configure IIS process lifetime/idle behavior for that requirement. Back up the SQL database, historical encryption keys and encrypted documents together and test recovery with the deployment identity. Replacing published code must preserve those data directories. Review [Key Protection and Recovery](#key-protection-and-recovery), the import limits, and client inputs below before handover.

## Known Limitations and Client Inputs

Representative source files and expected dataset volume are still needed. Fixed templates do not promise compatibility with arbitrary legacy spreadsheets. Batch limits are safety bounds, not performance guarantees; execution holds one serializable transaction for the whole batch and other writers may cause an atomic failure.

Confirm the meaning of existing clinical timestamps, approve audit/import/document retention and establish key backup/recovery ownership. Radiology needs to confirm the patient preparation guide, the films/CD and report releasing window, hours and claiming procedure, and whether the Basement 1 location and Local 208 shown on patient pages are current; the portal marks these as to be confirmed and shows no images. Approve whether patients may see full results before switching `PatientResults:ShowFullResults` on. The Filipino and Cebuano "Radiology" navigation labels need translator review. Departments, OPD and Malasakit guides, privacy/terms, official links and facility instructions remain hard-coded; decide whether they should become managed content. Clinical integrations, authoritative content and institutional deployment approval remain outside this application's current scope.
