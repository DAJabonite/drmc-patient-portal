# DRMC Patient Portal

ASP.NET Core 10 MVC and Razor Pages patient portal for Davao Regional Medical Center. Patients access linked hospital records, visits, medications, released reports and their ID wallet. Staff access is divided among laboratory, radiology, patient services and Red Star roles. Admin alone does not grant access to the confidential Red Star registry.

## Quick Start

1. On Windows, install Git, the .NET 10 SDK, SQL Server Express and SQL Server Management Studio (SSMS). [Clone and build](#1-clone-and-build) the portal.
2. [Create a local SQL database](#2-create-the-local-database), then [configure your PowerShell session](#3-configure-this-terminal) and [run the offline upgrade](#4-apply-migrations-and-initialize-protection).
3. [Start the HTTPS site](#5-run-and-check-the-website) at `https://localhost:7104`. [Create the first Admin](#6-create-the-first-admin-and-enroll-staff) or use the [demo accounts](#demo-accounts) to explore as a patient.

Use synthetic data for local development. The clone supplies application code, migrations and browser assets. Each installation has its own SQL database, accounts, uploaded files, keys and email settings. For deployment, see [Production Deployment](#production-deployment-windows-server--iis).

## Windows local setup

| Prerequisite | What to install |
| --- | --- |
| Git | [Git for Windows](https://git-scm.com/downloads) |
| .NET | [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) for Windows x64 with current security patches |
| SQL Server | [SQL Server Express](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) with **Database Engine Services**, instance **SQLEXPRESS**, Windows Authentication, and your Windows account as a local SQL administrator |
| SQL client | [SQL Server Management Studio](https://learn.microsoft.com/en-us/ssms/install/install), installed separately from the database engine |
| OCR runtime | [Microsoft Visual C++ Redistributable for x64](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist) for the bundled native OCR libraries |
| MFA | An authenticator app for the first Admin and invited staff |

The browser assets are checked in. Build and run from PowerShell.

### 1. Clone and build

From your preferred parent folder:

```powershell
git clone https://github.com/DAJabonite/drmc-patient-portal.git
Set-Location drmc-patient-portal
git branch --show-current
git log -1 --oneline
dotnet --version
dotnet restore DrmcPatientPortal.slnx
dotnet build DrmcPatientPortal.slnx -c Release --no-restore -warnaserror
```

Expect a .NET 10 SDK and a successful Release build. For EF model work, the optional EF CLI version matching the project's EF Core packages is `10.0.14`. Install it with `dotnet tool install --global dotnet-ef --version 10.0.14`, or use `dotnet tool update` with the same flags if it is already installed. Confirm with `dotnet ef --version`.

### 2. Create the local database

In SSMS, connect to `localhost\SQLEXPRESS` with **Windows Authentication**. For this local instance, enable **Trust Server Certificate** if needed. If SSMS cannot connect, check that the **SQL Server (SQLEXPRESS)** service is running.

For a new installation, open **New Query** against `master` and run this once:

```sql
CREATE DATABASE [DrmcPatientPortal_Dev]
COLLATE Latin1_General_100_BIN2;
```

The binary collation is required for protected lookup indexes. Verify the target and confirm that it has no application tables:

```sql
USE [DrmcPatientPortal_Dev];
SELECT DB_NAME() AS DatabaseName,
       DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS CollationName;
SELECT name FROM sys.tables ORDER BY name;
```

| Situation | What to do |
| --- | --- |
| Fresh installation | Create the empty database and complete the remaining steps. |
| Restart an existing installation | Reuse the existing database, storage, key ring and Admin. Repeat the terminal configuration in a new PowerShell window. |
| Upgrade an existing installation | Stop every portal process using it, back up SQL, private storage and the full key ring together, then run the offline upgrade with those same paths. Keep the original collation and keys. |
| Separate installation on the same PC | Choose a distinct database name and private storage root. Replace the example database name in the SQL and PowerShell commands. Clones share the project's User Secrets. |

### 3. Configure this terminal

Run from the repository root and keep this PowerShell window open for maintenance and startup. For an existing installation, use its original storage paths in place of the examples.

```powershell
$databaseName = 'DrmcPatientPortal_Dev'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ConnectionStrings__DefaultConnection = "Server=localhost\SQLEXPRESS;Database=$databaseName;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
$env:AdminBootstrap__Enabled = 'false'
$env:DevelopmentFixtures__Enabled = 'false'
$env:Notifications__Email__Provider = 'Console'
$env:Portal__PublicOrigin = 'https://localhost:7104'
$env:ResultStaging__Enabled = 'false'
$env:RedStar__WorkerEnabled = 'false'
$env:RedStar__RemindersEnabled = 'false'
$env:Logging__LogLevel__Microsoft = 'Warning'

$localStorage = Join-Path $env:LOCALAPPDATA "DRMC\PatientPortal\$databaseName"
$env:DataProtection__KeyRingPath = Join-Path $localStorage 'DataProtectionKeys'
$env:PatientDocuments__RootPath = Join-Path $localStorage 'PatientIdDocuments'
$env:PatientDocuments__TemporaryPath = Join-Path $localStorage 'TempUploads'
$env:LabReports__RootPath = Join-Path $localStorage 'LabReports'
$env:RadiologyReports__RootPath = Join-Path $localStorage 'RadiologyReports'
$env:RedStarLabReports__RootPath = Join-Path $localStorage 'RedStarLabReports'
$env:AdvisoryImages__RootPath = Join-Path $localStorage 'AdvisoryImages'
$env:Imports__StagingPath = Join-Path $localStorage 'ImportStaging'
```

Double underscores separate configuration sections. These session settings override tracked settings and Development User Secrets for the same keys. `TrustServerCertificate=True` is for this local SQL instance. Console prints account email and invitations as `[EMAIL OUTBOX]` entries in the terminal. Open the confirmation link to confirm the account.

#### Optional Gmail SMTP

Use Console for local account verification, password reset, and staff invitations without an inbox. To test actual delivery, choose a Gmail test sender, enable [2-Step Verification](https://support.google.com/accounts/answer/185833), and create an [App Password](https://myaccount.google.com/apppasswords). Run this from the repository root:

```powershell
$mailProject = 'src/DrmcPatientPortal/DrmcPatientPortal.csproj'
$mailAddress = Read-Host 'Gmail test sender address'
$mailPassword = Read-Host 'Google App Password' -AsSecureString
try {
    @{
        'Notifications:Email:Host' = 'smtp.gmail.com'
        'Notifications:Email:Port' = '587'
        'Notifications:Email:UseSsl' = 'true'
        'Notifications:Email:Username' = $mailAddress
        'Notifications:Email:FromAddress' = $mailAddress
        'Notifications:Email:FromName' = 'DRMC Patient Portal (Local Test)'
        'Notifications:Email:Password' = [System.Net.NetworkCredential]::new('', $mailPassword).Password
    } | ConvertTo-Json | dotnet user-secrets set --project $mailProject
    if ($LASTEXITCODE -ne 0) { throw 'Email configuration was not saved.' }
} finally {
    $mailPassword.Dispose()
    Remove-Variable mailPassword
}
$env:Notifications__Email__Provider = 'Smtp'
```

Restart with the HTTPS profile and use an inbox you control. For staff invitations, issue a fresh invitation from `/Admin/StaffInvitations` and check the sender, code, expiry, and signup link. Follow the signup and email confirmation links on the computer running the portal. Use `/Identity/Account/ResendEmailConfirmation` for an unconfirmed account. An invitation marked queued still needs the outbox worker to send it. To switch back, set `Notifications__Email__Provider` to `Console` and restart. Request new links when switching transports.

### 4. Apply migrations and initialize protection

Stop every portal process using this installation. From the repository root, confirm the SQL target and run the offline command:

```powershell
$target = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($env:ConnectionStrings__DefaultConnection)
Write-Host "Migration target: server=$($target.DataSource); database=$($target.InitialCatalog)"
if ($target.DataSource -ne 'localhost\SQLEXPRESS' -or $target.InitialCatalog -ne $databaseName) {
    throw 'Unexpected database target.'
}

dotnet run --project src/DrmcPatientPortal --launch-profile http -- redstar-protection upgrade
if ($LASTEXITCODE -ne 0) { throw 'Protection upgrade failed. Leave the portal stopped.' }
```

This command applies committed migrations, prepares registry and patient document protection, and exits. Expect **Private registry protection upgrade verified and complete**. If startup reports incomplete registry or document protection, stop the portal and rerun the offline command with the same database, key ring and storage.

Refresh **Tables** in SSMS and run:

```sql
USE [DrmcPatientPortal_Dev];
SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
SELECT Ready FROM dbo.RedStarProtectionStates WHERE Id = 1;
SELECT Phase FROM dbo.DocumentProtectionStates WHERE Id = 1;
SELECT COUNT(*) AS AccountCount FROM dbo.AspNetUsers;
SELECT COUNT(*) AS PatientRecordCount FROM dbo.PatientRecords;
```

Expect `20261106170233_ClinicalRedStarPrescriptionUpdates` as the latest migration, registry `Ready = 1`, and document `Phase = 3`. A fresh installation has zero accounts and patient records before bootstrap or demo seeding.

### 5. Run and check the website

From the repository root:

```powershell
dotnet dev-certs https --trust
dotnet run --project src/DrmcPatientPortal --launch-profile https
```

Open [https://localhost:7104](https://localhost:7104). Check the home page, `/Directory`, `/Advisories`, `/Identity/Account/Login` and `/Identity/Account/Register`. `/health/live` and `/health/ready` should return `Healthy`. On a fresh database the clinical lists start empty.

Stop the app with **Ctrl+C** before changing configuration. If port 7104 or the HTTPS profile's HTTP port 5095 is in use, stop the old process or switch to a free port and keep the origin and launch URL in sync. Reuse this same launch command for the rest of setup if you switch:

```powershell
$env:Portal__PublicOrigin = 'https://localhost:7105'
dotnet run --project src/DrmcPatientPortal --no-launch-profile --urls https://localhost:7105
```

For a quick HTTP check, run `.\run.ps1` from the repository root after preparing the database. It starts the default profile at `http://localhost:5095` and leaves your shell in `src\DrmcPatientPortal` when the app stops. Return to the root before using the commands above. Use the HTTPS launch for account links, QR and camera testing. A phone needs a reachable HTTPS test hostname with the matching `Portal__PublicOrigin` and allowed host.

### 6. Create the first Admin and enroll staff

Stop the site, keep the same database and storage settings, and temporarily enable bootstrap for one email:

```powershell
$env:AdminBootstrap__Enabled = 'true'
$env:AdminBootstrap__Email = Read-Host 'First Admin email'
dotnet run --project src/DrmcPatientPortal --launch-profile https
```

1. Register with that exact email. Leave the hospital record code blank and complete the identity and consent fields. Manual entry is available and an ID photo is optional.
2. With Console selected, open the confirmation link from the latest `[EMAIL OUTBOX]` entry. Copy the full URL and decode `&amp;` to `&` if copying from HTML. With SMTP selected, open the confirmation email. Use `/Identity/Account/ResendEmailConfirmation` if you need a new link.
3. Sign in and open `/Identity/Account/Manage/TwoFactorAuthentication`. Enroll the authenticator using its QR code or manual key, provide your current password when prompted, verify a current code, and store the recovery codes privately.
4. Stop and restart with bootstrap still enabled for the same email. Startup promotes the confirmed, MFA-enrolled account. Stop again, disable bootstrap and restart:

   ```powershell
   $env:AdminBootstrap__Enabled = 'false'
   Remove-Item Env:AdminBootstrap__Email -ErrorAction SilentlyContinue
   dotnet run --project src/DrmcPatientPortal --launch-profile https
   ```

5. Sign out and back in with a fresh authenticator code. Leave **Remember this device** unchecked for this check, then open `/Admin`.

Invite new staff at `/Admin/StaffInvitations`. Have each recipient register with the invited email and code, confirm email, enroll MFA, and complete a fresh MFA login. Manage access for eligible existing accounts at `/Admin/StaffAccess`.

| Role | Main scope |
| --- | --- |
| Admin | General administration, staff access, clinical records and public content |
| LabStaff | Laboratory records, items and reports |
| RadiologyStaff | Radiology studies and reports |
| PatientServicesStaff | Hospital record enrollment codes |
| RedStarStaff | Restricted clinic registry, reports, imports and exports, and eligible clinic prescriptions |

To rehearse standard patient enrollment, create two fictional records at `/Admin/Patients` with unique hospital numbers, verified structured names and birth dates. Issue each a code from `/Admin/RegistrationCodes` and register a separate account with matching details. Confirm both emails, then verify each account can see only its own visits, prescriptions and released reports. Download current general import templates from `/Admin/Imports` and restricted clinic templates from `/Admin/RedStarImports`.

## Demo Accounts

Prepare a separate, empty Development database such as `DrmcPatientPortal_Demo` with the same collation. Repeat the terminal configuration and offline upgrade with that database name so it gets its own persistent storage. Keep `AdminBootstrap__Enabled` off and select `Console`. Set both sets of demo credentials before the first app launch:

```powershell
$env:DevelopmentFixtures__Enabled = 'true'
$env:DevelopmentFixtures__Primary__Email = 'demo.patient@example.test'
$env:DevelopmentFixtures__Empty__Email = 'demo.empty@example.test'
$primaryPassword = Read-Host 'New password for the populated demo account' -AsSecureString
$emptyPassword = Read-Host 'New password for the empty demo account' -AsSecureString
try {
    $env:DevelopmentFixtures__Primary__Password = [System.Net.NetworkCredential]::new('', $primaryPassword).Password
    $env:DevelopmentFixtures__Empty__Password = [System.Net.NetworkCredential]::new('', $emptyPassword).Password
    dotnet run --project src/DrmcPatientPortal --launch-profile https
} finally {
    $primaryPassword.Dispose()
    $emptyPassword.Dispose()
    Remove-Variable primaryPassword, emptyPassword
    $env:DevelopmentFixtures__Enabled = 'false'
    Remove-Item Env:DevelopmentFixtures__Primary__Email, Env:DevelopmentFixtures__Primary__Password,
        Env:DevelopmentFixtures__Empty__Email, Env:DevelopmentFixtures__Empty__Password -ErrorAction SilentlyContinue
}
```

Choose a separate password for each account with at least eight characters, including an uppercase letter, a lowercase letter, a digit, and a symbol. Sign in at `/Identity/Account/Login` with the email and password you configured. The accounts are already confirmed and retain their credentials after the fixture settings are cleared. To rehearse staff access on the demo database, create a separate Admin and invite staff through the normal bootstrap and invitation steps.

| Account | What to verify |
| --- | --- |
| `demo.patient@example.test` | Maria Clara D. Santos has sample visits and prescriptions. Open **Visits** (`/Patient/Visits`), **Tagubilin** (`/Patient/Medications`), and **Red Star** (`/Patient/RedStar`). Select **View Tagubilin** on the refill card and verify it opens the served prescription `DEMO-RX-001`. Confirm attendance from the Red Star schedule when available. |
| `demo.empty@example.test` | Juan Miguel A. Dela Cruz is the empty-state patient. In a separate browser session, check that this account sees empty patient lists and cannot open Maria's prescription or Red Star schedule. |

### Rehearse encounters, Tagubilin, and Red Star

1. As Admin, create a fictional hospital patient with a unique hospital number, verified structured name, and date of birth. Issue a registration code and use matching details to register and confirm a separate patient account. Add an encounter under **Encounters** (`/Admin/ClinicalEncounters`), then link a prescription to it under **Prescriptions** (`/Admin/Prescriptions`). Verify the patient can open the encounter from **Visits** and the prescription from **Tagubilin**.
2. For the Red Star workflow, use a separate MFA-authenticated `RedStarStaff` account to link the fictional clinic record and verify its hospital mapping. In **Prescriptions**, select the mapped patient and encounter, confirm **Is Red Star medicine** is selected, and choose the active enrollment. Save the structured dosing and duration as **Prescribed**. To mark it **Served**, enter the actual dispensing date and quantity. For an eligible scheduled refill, 30 tablets dispensed at one tablet per day set the next refill 30 days after serving. As-needed prescriptions use a manually entered quantity and have no automatic refill date.
3. Open the linked patient's **Red Star** page and verify that the refill points to the same served Tagubilin prescription. Admin can manage prescriptions and `RedStarStaff` can manage prescriptions for verified mapped clinic patients. The confidential registry and its imports still require `RedStarStaff`. Download `Prescriptions_v3` from **Imports** for spreadsheet rehearsal and cancel and re-upload older queued prescription batches.
4. To generate in-portal reminders on the isolated demo database, set `RedStar__WorkerEnabled=true` and `RedStar__RemindersEnabled=true` and restart. The seed places the populated patient's refill seven days out at creation. Email for eligible refills requires a confirmed, opted-in patient and SMTP. Leave email opt-in off for Console rehearsal.

## Production Deployment (Windows Server & IIS)

This guide deploys the framework-dependent x64 application to Windows Server and IIS. The repository supplies application code, migrations, browser assets and deployment scripts; accounts, credentials, databases, certificates and uploaded files are external. Use synthetic data for rehearsal.

### Prerequisites

- Windows Server with IIS 10; Windows Server 2022/2025 are deployment targets. Install Anonymous Authentication, Application Initialization, and Management Scripts and Tools. Use elevated x64 Windows PowerShell 5.1 for the IIS helper.
- Install IIS first, then the x64 [.NET 10 Hosting Bundle](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), version **10.0.14 or a later supported 10.0 security patch**. Both `Microsoft.NETCore.App` and `Microsoft.AspNetCore.App`, plus `AspNetCoreModuleV2`, are required. Use the .NET 10 SDK on the build machine.
- SQL Server with a trusted SQL TLS certificate and a database created with **`Latin1_General_100_BIN2` collation**. Local rehearsal uses SQL Server 2025 Express. The maintenance identity needs schema migration permissions; the runtime identity needs application data and role-table access, migration-history reads and SQL application-lock access. Provision SQL logins separately, including the pool identity when using Windows authentication; the IIS helper does not grant SQL permissions.
- A DNS hostname and valid server-authentication HTTPS certificate with its private key in `LocalMachine\My`, plus an authenticated SMTP provider supporting STARTTLS.
- Visual C++ 2019-compatible runtime for the bundled native OCR libraries. Publish includes `x64/tesseract50.dll`, `x64/leptonica-1.82.0.dll`, SkiaSharp Windows assets, `tessdata/eng.traineddata` and `Services/OcrFixtures/probe.*`. Preserve these files, guide JSON, resource files and browser assets.

### Build and publish

From the repository root, with build and publish output outside the checkout:

```powershell
dotnet build DrmcPatientPortal.slnx -c Release -warnaserror --artifacts-path ../_scratch/build-artifacts
dotnet publish src/DrmcPatientPortal/DrmcPatientPortal.csproj -c Release -o ../_scratch/publish --artifacts-path ../_scratch/build-artifacts -warnaserror
```

Copy the complete publish output to the dedicated IIS publish directory. The generated `web.config` uses **InProcess** hosting. Configure application settings at the dedicated pool rather than adding secrets to `web.config` or tracked JSON. `run.ps1` is a local `dotnet run` convenience script; IIS runs the published application.

### Production configuration

Prepare a restricted **flat JSON object of environment-variable names and string values** for `SettingsFile`. Keep it outside publish and all mutable storage, accessible only to the required deployment identities. Do not commit it or print its contents. Double underscores separate configuration sections.

| Configuration names | Purpose |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | SQL Server, target database and authentication configuration. |
| `Portal__PublicOrigin`, `AllowedHosts` | Canonical HTTPS origin on the configured hostname and port 443; explicit allowed hosts without wildcards or ports. |
| `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT` | Production environment; enforced by the IIS helper. |
| `Identity__RequireConfirmedAccount`, `PortalOperations__MonitorWorkers`, `DevelopmentFixtures__Enabled` | Confirmed accounts, worker monitoring and exclusion of development fixtures; enforced by the helper. |
| `DataProtection__KeyRingPath` | Persistent complete key ring, including the protected Red Star blind-index key. |
| `DataProtection__CertificatePath`, `DataProtection__CertificatePasswordFile` | Readable RSA private-key certificate and its password file for certificate-based protection. |
| `DataProtection__DecryptionCertificates__N__Path`, `DataProtection__DecryptionCertificates__N__PasswordFile` | Historical certificates required to decrypt older keys. |
| `DataProtection__ProtectKeysWithDpapi` | Alternative Windows-identity-bound protection; do not enable together with certificate protection. |
| `PatientDocuments__RootPath`, `PatientDocuments__TemporaryPath` | Permanent encrypted ID images and temporary uploads. |
| `LabReports__RootPath`, `RadiologyReports__RootPath`, `RedStarLabReports__RootPath` | Separate persistent encrypted report stores. |
| `AdvisoryImages__RootPath`, `Imports__StagingPath` | Advisory images and encrypted spreadsheet-import staging. |
| `ResultStaging__Enabled`, `ResultStaging__LaboratoryPath`, `ResultStaging__RadiologyPath`, `ResultStaging__QuarantinePath` | Optional automated result ingestion; all three separate paths are required when enabled. |
| `Notifications__Email__Host`, `Notifications__Email__Username`, `Notifications__Email__Password` | Required authenticated SMTP connection. Supply these in restricted deployment settings. |
| `Notifications__Email__Port`, `Notifications__Email__UseSsl`, `Notifications__Email__Provider` | SMTP transport. Production requires authenticated SMTP with STARTTLS. |
| `Notifications__Email__FromAddress`, `Notifications__Email__FromName` | Optional sender overrides. Defaults are `redstarclinic@drmc.doh.gov.ph` and `DRMC Red Star Clinic / Patient Portal`. |
| `Notifications__Sms__Enabled`, `Notifications__Sms__Endpoint`, `Notifications__Sms__ApiKey`, `Notifications__Sms__SenderId` | Optional HTTPS SMS webhook delivery. |
| `AdminBootstrap__Enabled`, `AdminBootstrap__Email` | Temporary first-Admin signup for one bare email address. |
| `PatientResults__ShowFullResults` | Institution-approved disclosure of full laboratory/radiology PDFs; disabled by default. |
| `Proxy__KnownProxies__N` | Explicit trusted proxy IP addresses when a reverse proxy is present. |
| `Ocr__NativeLibraryPath`, `Ocr__TessDataPath` | Optional overrides for bundled native OCR libraries and language data. |

Use separate absolute mutable roots outside publish. Define their paths explicitly in the settings file so offline maintenance uses the same paths. The helper can also create non-overlapping child directories beneath `StorageRoot` for omitted storage settings; if using those defaults, resolve and reuse the resulting pool settings for maintenance. Retain those paths on subsequent deployments. Certificate/password files must also be outside `StorageRoot` and every mutable directory. The HTTPS certificate selected by `CertificateThumbprint` and the RSA key-protection certificate serve separate purposes.

The configured sender is used for account verification, staff invitations and opted-in Red Star refill reminders. Confirm that the production SMTP account is authorized to send as the configured address and verify the received From header before launch. Review the clinic-identifying sender and patient disclosure with DRMC privacy staff before enabling live refill email reminders. Keep `RedStar__RemindersEnabled=false` until that review is complete. This pauses both in-portal and email reminders. Omit both sender overrides from the desired IIS settings to adopt both application defaults.

Production rejects missing required configuration, unsafe or overlapping storage paths, invalid notification configuration and development fixtures. The helper treats `SettingsFile` as desired configuration: omitted stale application-owned settings are removed, while unrelated variables and existing storage/protection bindings are preserved. Include every intended nondefault setting. Indexed certificate/proxy settings support indices 0 through 31.

### Configure IIS

Prepare the publish files, restricted settings and certificate/password files before invoking the helper. From the repository root, in elevated x64 Windows PowerShell 5.1, assign the deployment-specific parameters without placing secrets in command history:

```powershell
$iisParameters = @{
    SiteName              = $siteName
    PoolName              = $poolName
    PublishPath           = $publishPath
    StorageRoot           = $storageRoot
    HostName              = $hostName
    CertificateThumbprint = $certificateThumbprint
    SettingsFile          = $settingsFile
}
.\scripts\configure-iis.ps1 @iisParameters -WhatIf
.\scripts\configure-iis.ps1 @iisParameters
```

The helper configures a dedicated HTTPS/SNI site on port 443 and a dedicated x64 ApplicationPoolIdentity pool with No Managed Code, loaded profile, AlwaysRunning/preload, one worker, zero idle timeout, no elapsed-time recycle and no overlapping rotation. Application shutdown allows 60 seconds, pool shutdown 90 seconds; configuration waits up to 100 seconds for the target pool to stop. **The pool remains stopped after configuration.**

Anonymous Authentication uses the pool identity; registered Windows/Basic/Digest authentication is disabled for the public cookie-authenticated portal. The pool receives ReadAndExecute on publish, Modify on mutable directories and ReadAndExecute on certificate/password files. New private roots retain SYSTEM/Administrators control. Existing specific service/admin grants are preserved; broad/conflicting grants, links and unexpected protected descendant ACLs require review. The helper does not recursively reset ACLs.

Keep existing storage and certificate bindings. `-AllowStoragePathChange` acknowledges an already-planned **offline** transfer; it does not move files. The helper refuses automatic key-protection-mode or active-certificate changes.

### Prepare schema and create the first Admin

Follow this order for installation or upgrade:

1. For an existing installation, prevent new imports, drain active import and reminder workers, and cancel queued or review-pending imports that predate validation **3.0.0** or use the old prescription template. Retain the originals for re-upload using `Prescriptions_v3` for prescriptions.
2. Stop every portal instance. Back up the database, private files, full key ring including `redstar-blind-index-v1.protected`, certificate material and configuration as one consistent set. For a new installation, create an empty database with `Latin1_General_100_BIN2` collation.
3. Publish/copy the application and configure IIS as above. Preserve existing external paths, or complete the planned offline file/key transfer. Leave the pool stopped.
4. In a restricted maintenance process, load the **same Production, storage, key and certificate settings** and the maintenance SQL connection. From the publish directory run:

   ```powershell
   dotnet DrmcPatientPortal.dll redstar-protection upgrade
   ```

   This command applies migrations and initializes/verifies protection. It is safe to rerun after success or interruption. Normal Production web startup verifies preparation and does **not** apply migrations. Return to the intended runtime SQL identity before starting IIS.
5. For the first Admin only, configure `AdminBootstrap__Enabled` and `AdminBootstrap__Email` in the settings file, apply the helper, then start the dedicated pool. Verify `/health/ready`. Register that email through the portal, follow the delivered confirmation link and enroll an authenticator. Restart the pool to promote the fully enrolled account. Incomplete enrollment survives restart; an existing Admin prevents promotion of another configured account.
6. Disable bootstrap in the settings file, apply the helper and start the pool again. Sign out and complete a fresh password-plus-authenticator login. Verify Admin access and readiness. Existing installations use their existing Admin. Re-upload canceled imports using the current templates.

Admin creates email-bound invitations for scoped staff roles. Each invited user must confirm email, enroll MFA and complete a fresh MFA login before staff access activates. Patient accounts link through issued registration codes and matching hospital identity details. Red Star account linking and hospital mapping require its separately authorized clinic role.

### Operations and release checks

Verify `/health/live` and `/health/ready`, including readiness after the two-minute worker startup grace period. Exercise account email delivery, role-specific workflows, report access, OCR, restart persistence and permission denials with synthetic data before using real records. Readiness alone does not prove external email delivery.

Retain the database and all protected storage/key/certificate material together. Replacing a checkout or publish directory does not reset accounts or encrypted storage. Rollback requires restoring the matched backup set. Current-user DPAPI requires its original Windows identity/profile; migrate its protection deliberately under that identity before changing identities. Rehearse coordinated backup restoration before launch.

Real IIS authentication, bindings, certificate trust, ACLs, profile loading, preload and recycle behavior require validation on the target server; local HTTPS Kestrel checks do not establish them. External SMTP/SMS delivery, backup restoration and hospital concurrency remain **UNVERIFIED** until separately exercised. Prior readiness work used planning limits of 50,000 patients and 5,000 Red Star records; these are not enforced capacity guarantees or evidence of hospital-scale performance. Staff/Admin interfaces are English; public/patient interfaces support English, Filipino and Cebuano.
