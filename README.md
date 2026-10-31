# DRMC Patient Portal

An ASP.NET Core 10 website for Davao Regional Medical Center patient services, with English, Filipino and Cebuano interfaces. Patients can view their linked hospital records, visits, medications, laboratory reports, radiology information and ID wallet. Staff manage records, enrollment codes, public content and controlled spreadsheet imports.

This repository provides application code and migrations. It does not connect automatically to the hospital's existing systems or contain a production database. Use synthetic records for development. Hospital content, disclosure settings, delivery providers and deployment require institutional approval and rehearsal.

Start with [Windows local setup](#windows-local-setup), then [create the first Admin](#6-create-the-first-admin-and-enroll-staff). Operators should also read [Deployment and maintenance](#deployment-and-maintenance).

## What is included

- Patient ownership checks, confirmed accounts and staff MFA.
- Encrypted private ID images, laboratory PDFs and import staging.
- Hospital record codes and email-bound staff invitations.
- Staff management of patients, visits, medicines, results, doctors, advisories and Institutional Guides.
- A separately authorized Red Star clinic registry; Admin alone cannot access its records.
- SQL-backed email queues, revocable sessions, access auditing and operational health probes.
- Integration tests against disposable SQL Server databases and GitHub Actions CI.

Full laboratory values and radiology reports are hidden by default. There is no radiology image viewer or live hospital-system integration. Review [deployment requirements](#deployment-and-maintenance) before using real patient records.

## Prerequisites

For the Windows walkthrough, install:

| Tool | Purpose |
| --- | --- |
| [Git](https://git-scm.com/downloads) | Clone and update the source |
| [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | Build and run the application and tests |
| SQL Server Express | Local database engine; use Windows authentication |
| [SSMS](https://learn.microsoft.com/en-us/ssms/install/install) | Create the database and inspect tables |
| EF Core CLI 10.0.11 | Apply the committed schema migrations |
| Authenticator app | Enroll the first Admin and staff in MFA |

SQL Server and SSMS are separate installations. Install Express with **Database Engine Services**, instance **SQLEXPRESS**, Windows authentication and your Windows user as a local SQL administrator. In SSMS, connect to `localhost\SQLEXPRESS` with Windows Authentication. If its local certificate is untrusted, enable **Trust Server Certificate** for development. If the engine is unreachable, check that **SQL Server (SQLEXPRESS)** is running; SSMS alone does not run the engine.

The application uses Razor Pages/MVC, Identity, Bootstrap, EF Core, HtmlSanitizer, ExcelDataReader, Tesseract OCR and QRCoder. Browser assets are checked in; no npm build is required.

## Windows local setup

These steps create an empty development installation. Keep the same PowerShell window open for configuration and running the app. Existing installations must use the [upgrade procedure](#upgrades-and-recovery) with their original database, keys and private storage.

| Situation | What to do |
| --- | --- |
| Restart an existing installation | Repeat this terminal's configuration, select the email transport, and run the app. Keep the same database and private storage; use the existing Admin. |
| Fresh installation on another computer | Complete steps 1–6 with a new database and private storage. Configure that installation's email sender if real delivery is needed. |
| Another clone on the same computer | Choose a separate database and private storage for a separate installation. Cloning does not reset accounts or shared User Secrets. |

A clone supplies code and migrations, not accounts, SMTP credentials, uploaded files or encryption keys. Bootstrap is a one-time account setup for a new installation, not a step to repeat on every restart.

### 1. Create an empty database

In SSMS connect to `localhost\SQLEXPRESS` using Windows Authentication. Open **New Query** against `master` and run this once:

```sql
CREATE DATABASE [DrmcPatientPortal_Dev]
COLLATE Latin1_General_100_BIN2;
```

The binary collation is required for protected lookup indexes. You can instead use **Databases → New Database → Options → Collation**. Do not change the collation of a populated database to repeat this exercise.

Verify your target before continuing:

```sql
USE [DrmcPatientPortal_Dev];
SELECT DB_NAME() AS DatabaseName,
       DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS CollationName;
SELECT name FROM sys.tables ORDER BY name;
```

Expect `DrmcPatientPortal_Dev`, `Latin1_General_100_BIN2` and no application tables. Each developer can choose a different database name; replace it consistently below.

### 2. Clone and build

From your preferred parent folder:

```powershell
git clone https://github.com/DAJabonite/drmc-patient-portal.git
Set-Location drmc-patient-portal
git branch --show-current
git log -1 --oneline

dotnet --version
dotnet restore DrmcPatientPortal.slnx
dotnet build DrmcPatientPortal.slnx -c Release --no-restore -warnaserror
dotnet ef --version
```

Expect a successful build without warnings. Stop if restore or build fails. If EF is missing, install it:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.11
```

If a different EF version is already installed, use `dotnet tool update --global dotnet-ef --version 10.0.11`, then check its version again.

`DrmcPatientPortal.slnx` groups the application and integration tests. Restore downloads NuGet packages; it does not restore a database. GitHub contains no local accounts, secrets or encrypted uploads. Generated build and test output is ignored.

### 3. Configure this terminal

Run from the repository root. These settings apply to child processes launched from this PowerShell window. Repeat them in a new window before running the same installation.

```powershell
$databaseName = 'DrmcPatientPortal_Dev'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:AdminBootstrap__Enabled = 'false'
$env:DevelopmentFixtures__Enabled = 'false'
$env:ConnectionStrings__DefaultConnection = "Server=localhost\SQLEXPRESS;Database=$databaseName;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
$env:Logging__LogLevel__Microsoft = 'Warning'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:Notifications__Email__Provider = 'Console'
$env:Portal__PublicOrigin = 'https://localhost:7104'

# Persistent private storage outside Git and the web root.
$localStorage = Join-Path $env:LOCALAPPDATA "DRMC\PatientPortal\$databaseName"
$env:DataProtection__KeyRingPath = Join-Path $localStorage 'DataProtectionKeys'
$env:PatientDocuments__RootPath = Join-Path $localStorage 'PatientIdDocuments'
$env:PatientDocuments__TemporaryPath = Join-Path $localStorage 'TempUploads'
$env:LabReports__RootPath = Join-Path $localStorage 'LabReports'
$env:Imports__StagingPath = Join-Path $localStorage 'ImportStaging'
```

`TrustServerCertificate=True` is for this local SQL instance. Deployed SQL connections need a trusted certificate and validation. The explicit `Console` transport keeps this walkthrough local: messages are printed as `[EMAIL OUTBOX]`, not delivered to an inbox. Opening the printed confirmation link confirms the account; bootstrap and terminal output do not confirm it automatically.

Double underscores separate configuration sections. These values override Development user-secrets and tracked settings. Clones share the project's `UserSecretsId`; using per-terminal configuration avoids pointing another checkout at the wrong database.

#### Optional: deliver email with Gmail

Keep `Console` for development without an email account. For real confirmation, password-reset and staff-invitation delivery, configure SMTP before creating the test messages. An omitted `Notifications:Email:Provider` defaults to Console in Development and Smtp elsewhere; explicit values are case-insensitive. Console is rejected outside Development. Smtp requires a host, valid port, username, password, valid sender address and TLS enabled in every environment.

1. Choose a test Gmail sender account, enable [Google 2-Step Verification](https://support.google.com/accounts/answer/185833), and generate an [App Password](https://myaccount.google.com/apppasswords) named `DRMC Portal`. This is the portal's sender account; recipients do not need its credentials. If App Passwords are unavailable for that account, use another SMTP account rather than its normal Google password.
2. From the repository root, save these settings in local .NET User Secrets. The password prompt is hidden, and the generated App Password goes through standard input instead of shell history. The command updates these keys without replacing unrelated secrets:

```powershell
$mailProject = Join-Path (Get-Location) 'src/DrmcPatientPortal/DrmcPatientPortal.csproj'
$mailAddress = Read-Host 'Gmail sender address'
$mailPassword = Read-Host 'Google App Password' -AsSecureString
try {
    @{
        'Notifications:Email:Host' = 'smtp.gmail.com'
        'Notifications:Email:Port' = '587'
        'Notifications:Email:UseSsl' = 'true'
        'Notifications:Email:Username' = $mailAddress
        'Notifications:Email:FromAddress' = $mailAddress
        'Notifications:Email:Password' = [System.Net.NetworkCredential]::new('', $mailPassword).Password
    } | ConvertTo-Json | dotnet user-secrets set --project $mailProject
    if ($LASTEXITCODE -ne 0) { throw 'Email configuration was not saved.' }
} finally {
    $mailPassword.Dispose()
    Remove-Variable mailPassword
}
$env:Notifications__Email__Provider = 'Smtp'
```

Port 587 with `UseSsl=true` uses STARTTLS. Grouping whitespace is removed from Gmail App Passwords only; other SMTP passwords are preserved. Settings remain directly under `Notifications:Email`, without an extra `Smtp` section. Each developer configures credentials locally; do not commit credentials or copy them into shared instructions.

Portal emails use the display name **DRMC Patient Portal** with the configured sender address. Optional `Notifications:Email:FromName` overrides that name (single line only); Gmail controls its final inbox presentation and exposes the address in sender details. The shared layout includes HTML, plain text, and the existing DRMC seal embedded as a MIME `cid:` image, with no public image host or template service. Account and invitation content follows the request's English, Filipino or Cebuano language; scheduled appointment reminders remain neutral English. Existing sending limits still apply.

3. Continue with the remaining setup steps and use the HTTPS profile below. If the app is already running, stop it with Ctrl+C before restarting. After repeating step 3 in a new terminal, set `Notifications__Email__Provider` to `Smtp` again to select real delivery. To return to terminal-only delivery, set it to `Console` and restart.
4. Once an Admin is ready, create an invitation at `/Admin/StaffInvitations` for an inbox you control that is not already registered. Verify receipt, including the spam folder, then follow the signup link, confirm the new account's email, and enroll MFA. The email includes the role, one-time code, expiry in Philippine time, and a link that fills the code. The printable invitation page retains the QR code. An existing unconfirmed account can use `/Identity/Account/ResendEmailConfirmation` to test delivery.

The invitation page's queued message means the outbox accepted it, not that an inbox received it. Workers must remain enabled (`AccountEmail:WorkerEnabled` and `Notifications:Outbox:WorkerEnabled`, both enabled by default). Messages already processed by Console do not resend after switching to SMTP; issue a new invitation or request a new confirmation link. Automated tests force isolated transports and never use these credentials for live delivery.

For a controlled inbox check, verify the sender name/address, inline seal, readable layout, action and fallback destinations, and invitation code/expiry. Check account messages in each supported language and reminders for neutral content. Browser previews at 320px, 390px and desktop widths help inspect layout but do not validate Gmail or another email client. Keep synthetic previews outside Git; older queued HTML-only messages remain deliverable without a schema change.

For this walkthrough, open emailed links on the computer running the portal. Other devices need a reachable HTTPS test hostname, matching `Portal:PublicOrigin`, and appropriate allowed hosts. Production uses deployment-managed credentials and public HTTPS settings from [Deployment and maintenance](#deployment-and-maintenance), not developer User Secrets.

### 4. Apply migrations and initialize protection

Stop every portal process using this installation first. Confirm the exact SQL target, then run:

```powershell
$target = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($env:ConnectionStrings__DefaultConnection)
Write-Host "Migration target: server=$($target.DataSource); database=$($target.InitialCatalog)"
if ($target.DataSource -ne 'localhost\SQLEXPRESS' -or $target.InitialCatalog -ne $databaseName) {
    throw 'Unexpected database target'
}

dotnet ef database update --project src/DrmcPatientPortal -- --environment Development
if ($LASTEXITCODE -ne 0) { throw 'Migration failed; do not start the portal.' }

# Required even for an empty database. This command exits after maintenance.
dotnet run --project src/DrmcPatientPortal --launch-profile http -- redstar-protection upgrade
if ($LASTEXITCODE -ne 0) { throw 'Protection upgrade failed; do not start the portal.' }
```

Expect EF's `Done.` and **Private registry protection upgrade verified and complete**. The second command initializes/verifies registry protection and performs checkpointed ID-document maintenance. Ordinary startup checks its completion marker and does not migrate SQL or scan the document collection.

If startup says **Private registry protection is incomplete** or **Patient document protection is incomplete**, finish the offline command using the same key directory and storage. Do not delete keys or bypass the marker. See [Upgrades and recovery](#upgrades-and-recovery).

Refresh **Tables** in SSMS and check:

```sql
USE [DrmcPatientPortal_Dev];
SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
SELECT Ready FROM dbo.RedStarProtectionStates WHERE Id = 1;
SELECT Phase FROM dbo.DocumentProtectionStates WHERE Id = 1;
SELECT COUNT(*) AS AccountCount FROM dbo.AspNetUsers;
SELECT COUNT(*) AS PatientRecordCount FROM dbo.PatientRecords;
```

The latest committed migration is `20261028015220_AddStagedResultReports`. Expect registry `Ready = 1`, document `Phase = 3`, and zero accounts and patient records on a fresh installation. Never create a replacement Initial migration or invent the tables manually.

The structured-name migration preserves exact old names in `LegacyFullName`. Staff must verify missing structured names before issuing enrollment codes. Names erased by an earlier deployed build require recovery from a verified backup or authoritative source; this upgrade cannot recreate them. Reconcile by stable patient-record ID and hospital identifiers, without guessing name splits or overwriting verified fields.

### 5. Run and check the website

Keep the same terminal open:

```powershell
dotnet dev-certs https --trust
dotnet run --project src/DrmcPatientPortal --launch-profile https
```

Open [https://localhost:7104](https://localhost:7104), matching `Portal:PublicOrigin` above. Check the home page, `/Directory`, `/Advisories`, `/Identity/Account/Login` and `/Identity/Account/Register`. Empty clinical and directory lists are expected until staff add records.

Both `/health/live` and `/health/ready` should return `Healthy`. Readiness checks SQL, private-storage access, document protection, OCR initialization and worker activity. It is not proof of successful SMTP delivery or a complete backup restoration.

Stop with **Ctrl+C** before changing configuration. An already-running process does not pick up new environment variables. If the port is occupied, stop the old portal or use a separate port:

```powershell
$env:Portal__PublicOrigin = 'https://localhost:7105'
dotnet run --project src/DrmcPatientPortal --no-launch-profile --urls https://localhost:7105
```

Verify the new process started before relying on browser responses. `.\run.ps1` is another launcher; it does not create the database schema.

A phone's `localhost` refers to the phone; camera/QR rehearsal on another device needs a reachable HTTPS test host and the correct public origin. If using the alternate port above, reuse its `--no-launch-profile --urls https://localhost:7105` command for every restart in step 6. Otherwise free port 7104 and reset `Portal__PublicOrigin` to `https://localhost:7104` before using the `https` profile.

For development SQL in Docker, set a strong local `MSSQL_SA_PASSWORD` and run `docker compose up -d --wait`. Connect to `localhost,1433` with SQL authentication, create the same binary-collation database and adjust the connection string. The SQL-only Compose service uses Developer edition for development/testing. `docker compose down -v` destroys its data.

Ubuntu 24.04 x64 application hosts must run [scripts/setup-ocr-ubuntu.sh](scripts/setup-ocr-ubuntu.sh) and configure `Ocr__NativeLibraryPath=/opt/drmc/ocr`, `Ocr__TessDataPath=/usr/share/tesseract-ocr/5/tessdata` and `LD_LIBRARY_PATH=/opt/drmc/ocr/x64`. Native macOS application execution is unverified; the optional Compose `portal` profile provides a Linux application image. For a disposable reset, stop the app, confirm its exact SQL/database and private paths, then recreate only that installation. A new clone does not reset SQL or shared user-secrets. Never delete keys while retaining encrypted records.

### 6. Create the first Admin and enroll staff

The first Admin uses a temporary exemption because nobody can issue their enrollment code yet. Stop the app, keep the database/storage settings above, then run:

```powershell
$env:AdminBootstrap__Enabled = 'true'
$env:AdminBootstrap__Email = Read-Host 'First Admin email'
dotnet run --project src/DrmcPatientPortal --launch-profile https
```

1. Register with exactly that email and leave the hospital record code blank. Accept the notice/terms and complete identity details; manual entry is available and an ID photo is optional. Startup never creates a default account/password.
2. With Console selected, find the newest **`[EMAIL OUTBOX]`** confirmation link in PowerShell. Copy the complete URL inside `href`, replace `&amp;` with `&`, then open it. With Smtp selected, open the confirmation email instead. If needed, request a new link at `/Identity/Account/ResendEmailConfirmation`.
3. Sign in and open `/Identity/Account/Manage/TwoFactorAuthentication`. Enroll an authenticator using its QR/manual key and a current code; save the recovery codes privately. If restarting before confirmation/MFA is complete, temporarily disable bootstrap to finish setup.
4. Once confirmed and MFA-enabled, stop and restart with bootstrap enabled for the same email. Startup promotes that eligible account. The no-Admin check governs the registration-code exemption, not promotion of an existing account. Startup never sends an invitation email.
5. Stop again, disable bootstrap, and restart:

```powershell
$env:AdminBootstrap__Enabled = 'false'
Remove-Item Env:AdminBootstrap__Email -ErrorAction SilentlyContinue
dotnet run --project src/DrmcPatientPortal --launch-profile https
```

Remove bootstrap settings from persistent configuration too. Sign out/in using a fresh authenticator code, leaving **Remember this device** unchecked, then open `/Admin`. Password-only and remembered-browser sessions do not qualify for staff access. Recovery codes are single-use; generating replacements invalidates the old set.

An Admin uses `/Admin/StaffInvitations` to invite colleagues by email/role. Console prints the invitation locally; Smtp sends it to the specified inbox. They register using that same email and invitation, confirm email and enroll MFA; the invited role then activates. Existing confirmed accounts use `/Admin/StaffAccess`. Role changes take effect on the next request and are audited.

| Role | Main scope |
| --- | --- |
| Admin | General administration, staff access and content; private-clinic access requires RedStarStaff too |
| LabStaff | Laboratory records, items and reports |
| RadiologyStaff | Radiology studies |
| PatientServicesStaff | Hospital record enrollment codes |
| RedStarStaff | Private-clinic registry, imports and exports |

For rehearsal, create two fictional records at `/Admin/Patients`, including verified first/last names and birth dates. Issue each a code from `/Admin/RegistrationCodes`, register separate emails with matching details and confirm both accounts. Add visits, labs, prescriptions and radiology through Administration; check that each patient sees only their own records. General signup requires a hospital record code by default.

Doctors, advisories and Institutional Guides have their own Admin modules. General spreadsheet imports use `/Admin/Imports`; the restricted clinic uses `/Admin/RedStar` and `/Admin/RedStarImports`. Download the templates from those modules and reconcile source rows before approval. General imports are limited to 20 MiB/10,000 rows; private-clinic imports to 10 MiB. Back up persistent staging and keys with SQL.

## Test and validate changes

Tests create and remove uniquely named databases. Use a local SQL login allowed to create databases; omit the database name:

```powershell
$env:DRMC_TEST_SQLSERVER = 'Server=localhost\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet test DrmcPatientPortal.slnx -c Release --logger 'trx;LogFileName=results.trx'
dotnet ef migrations has-pending-model-changes --project src/DrmcPatientPortal
dotnet list DrmcPatientPortal.slnx package --vulnerable --include-transitive
```

Do not run tests against a production server. Build before testing when using `--no-build`. Development fixtures are opt-in: set `DevelopmentFixtures__Enabled=true` and explicit `DevelopmentFixtures__Primary__Email`/`__Password` and `DevelopmentFixtures__Empty__Email`/`__Password` values against an empty development installation. Fixtures never run outside Development.

GitHub Actions requires a clean Release build, a matching EF model, OCR fixtures and SQL integration tests. Production changes should pass `build-and-test` and receive an independent PR approval under the repository's protected-branch rules.

## Deployment and maintenance

Publish the reviewed commit:

```powershell
dotnet publish src/DrmcPatientPortal -c Release -o ./output/publish
```

Production requires confirmed accounts, a real public HTTPS origin, explicit allowed hosts, encrypted persistent keys and SMTP with TLS. Keep fixtures disabled. Configure settings in the deployment system/app pool, not just an interactive terminal. Required and optional controls include:

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | Approved production SQL with certificate validation and runtime DML permissions; use a separate schema operator |
| `Portal__PublicOrigin` / `AllowedHosts` | Public HTTPS origin and explicit semicolon-separated hosts; no wildcard hosts |
| `DataProtection__CertificatePath` / `__CertificatePasswordFile` | RSA PFX and optional password file outside Git/web/key directories; grant the app identity read access |
| `DataProtection__ProtectKeysWithDpapi` | Alternative Windows current-user protection; requires the original account, machine and profile for recovery |
| `DataProtection__DecryptionCertificates__0__Path` / `__PasswordFile` | Historical decryption certificates during rotation; retain old keys/certificates as long as data/backups require them |
| `Notifications__Email__Provider` | Omit or set `Smtp` outside Development; Console is rejected |
| `Notifications__Email__Host`, `__Port`, `__UseSsl`, `__FromAddress`, `__Username`, `__Password` | SMTP host, valid port, username, password and valid sender are required; `UseSsl=true` is mandatory whenever Smtp is selected |
| `Notifications__Sms__Enabled` | Default `false`; an enabled provider requires its HTTPS endpoint/API key; no automatic patient SMS producer exists |
| `PatientResults__ShowFullResults` | Default `false`; hospital approval required for full laboratory/radiology disclosure |
| `RedStar__WorkerEnabled` / `__RemindersEnabled` | Both default `false`; enable only after clinic/reminder-delivery verification |
| `WebsiteUsage__Enabled` | Default `false`; requires approval of collection purpose; aggregate retention defaults to 13 months |
| `PortalOperations__MinimumFreeDiskMb` / `__QueueWarningMinutes` | Readiness reserve (256 MiB) and queued-delivery warning threshold (5 minutes) |
| `PrivateRetention__Enabled`, `__ApprovedPolicyId`, `__OrphanGraceDays` | Disabled by default; approved policy ID and 7–3650 day grace required to enable orphan cleanup |

### IIS

Install IIS with Application Initialization and the matching .NET 10 Hosting Bundle. Use a dedicated x64 pool, No Managed Code, Load User Profile, AlwaysRunning/preload and no idle shutdown. Workers depend on the process staying active. See [Microsoft's IIS guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0).

[scripts/configure-iis.ps1](scripts/configure-iis.ps1) provisions the pool, SNI HTTPS binding and private-directory permissions. Run elevated Windows PowerShell 5.1, preview with `-WhatIf`, then apply reviewed values:

```powershell
.\scripts\configure-iis.ps1 -SiteName DrmcPortal -PoolName DrmcPortal `
  -PublishPath 'C:\DRMC\releases\REVIEWED_COMMIT' -StorageRoot 'D:\DRMC\portal-data' `
  -HostName 'portal.example.org' -CertificateThumbprint 'REPLACE_WITH_40_HEX_CHARACTERS' `
  -SettingsFile 'C:\DRMC-Secrets\portal-settings.json' -WhatIf
```

The settings file must be a restricted flat JSON object of environment-variable names and string values, supplying the connection, origin/hosts, key-protection certificate and SMTP settings above. The script sets both .NET environment variables to Production, requires HTTPS, disables bootstrap/fixtures, enforces confirmation/TLS and keeps the pool stopped for maintenance. It does not install certificates, grant SQL permissions or run migrations. Inspect existing descendant ACLs and grant certificate-file access separately.

Private paths are derived from `StorageRoot`: keys → `keys`, ID images → `patient-documents`, staged IDs → `temporary-uploads`, PDFs → `lab-reports`, staging → `imports`. Keep these paths and all historical certificates identical during offline maintenance and runtime. A virtual pool identity uses `IIS APPPOOL\<pool>` for local SQL; remote SQL needs a DBA-approved machine/domain identity. Never give the runtime schema ownership or `db_owner`.

### Upgrades and recovery

Stop/drain **all** old processes, take a coordinated backup of SQL/private storage/the complete key ring, and preserve private certificates/passwords separately. Load the deployment's exact configuration and persistent paths under the authorized maintenance identity, then run from the published directory:

```powershell
dotnet DrmcPatientPortal.dll redstar-protection upgrade
```

Do not start traffic after a failure. Rerun with the same settings to resume document checkpoints. Normal startup verifies one canary instead of scanning the collection. An explicit full rescan is available offline with `patient-documents verify`; do not edit completion markers or delete unreadable keys/files. The production-readiness migration blocks downgrade; restore a matching coordinated pre-upgrade backup for rollback.

Start one new process, verify readiness, old ID fronts/backs, protected PDFs, ownership and sign-in before restoring traffic. Rehearse SQL, document, key-ring and certificate restoration together in an isolated deployment. Keep `DRMC.PatientPortal` as the protection application name and retain every historical key. Encryption cannot recover records after their keys are lost.

Private-file cleanup must remain disabled until the records owner approves retention and legal holds. Once enabled, it checks references, observes an unreferenced file in SQL and waits the full grace period before deletion; file age alone is insufficient. Clinical records, clinic archives, audit history and backups require separate approved retention procedures.

### Monitoring and release checks

Monitor HTTPS `/health/live` and `/health/ready` externally. They return generic status, with readiness also checking SQL, private storage, disk reserve, OCR and worker progress. Collect the `DRMC.PatientPortal.Operations` .NET meter and sanitized warnings for queue counts/age, terminal delivery failures and disk capacity. Restrict/rate-limit probes at the edge. A healthy process does not prove successful provider delivery.

Before hospital rollout, rehearse registration, real QR/camera fallback, mobile navigation, staff MFA, report downloads, delivery outages, IIS restart/recycling/idle behavior, realistic storage/import volumes and complete restoration. Record responsible operators and institutional approvals. A passing build alone does not establish deployment readiness.

## Current integration limitations

Imports use fixed templates and need representative source files and realistic volumes before rollout. The portal has no automatic connection to existing hospital systems. Clinical timestamp meanings and institutional translations still need validation.

Institutional Guides can manage OPD and Malasakit guidance. Hospital owners must review that content, radiology instructions, privacy/terms, official links and support contacts. Full-results disclosure and usage collection remain explicit opt-ins.

Registration commits its database changes and queued verification email together. A confirmed rollback removes new ID files; an uncertain commit requires recovery checks. Later delivery failures remain queued/failed for operator attention and do not roll back the registered account. Shared recipient limits cover reset/resend requests across processes and IPs; other request/concurrency limits remain per process.

Clinical dates/times are Manila wall time; system/audit timestamps and advisory publication/expiry use UTC. Future advisories stay private; expired notices leave current lists and urgent banners, while direct links show an archive notice.
