# DRMC Patient Portal

ASP.NET Core 10 MVC and Razor Pages patient portal for Davao Regional Medical Center. Patients access linked hospital records, visits, medications, released reports and their ID wallet. Staff access is divided among laboratory, radiology, patient services and Red Star roles. Admin alone does not grant access to the confidential Red Star registry.

This guide deploys the framework-dependent x64 application to Windows Server and IIS. The repository supplies application code, migrations, browser assets and deployment scripts; accounts, credentials, databases, certificates and uploaded files are external. Use synthetic data for rehearsal.

## Prerequisites

- Windows Server with IIS 10; Windows Server 2022/2025 are deployment targets. Install Anonymous Authentication, Application Initialization, and Management Scripts and Tools. Use elevated x64 Windows PowerShell 5.1 for the IIS helper.
- Install IIS first, then the x64 [.NET 10 Hosting Bundle](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), version **10.0.14 or a later supported 10.0 security patch**. Both `Microsoft.NETCore.App` and `Microsoft.AspNetCore.App`, plus `AspNetCoreModuleV2`, are required. Use the .NET 10 SDK on the build machine.
- SQL Server with a trusted SQL TLS certificate and a database created with **`Latin1_General_100_BIN2` collation**. Local rehearsal uses SQL Server 2025 Express. The maintenance identity needs schema migration permissions; the runtime identity needs application data and role-table access, migration-history reads and SQL application-lock access. Provision SQL logins separately, including the pool identity when using Windows authentication; the IIS helper does not grant SQL permissions.
- A DNS hostname and valid server-authentication HTTPS certificate with its private key in `LocalMachine\My`, plus an authenticated SMTP provider supporting STARTTLS.
- Visual C++ 2019-compatible runtime for the bundled native OCR libraries. Publish includes `x64/tesseract50.dll`, `x64/leptonica-1.82.0.dll`, SkiaSharp Windows assets, `tessdata/eng.traineddata` and `Services/OcrFixtures/probe.*`. Preserve these files, guide JSON, resource files and browser assets.

## Build and publish

From the repository root, with build and publish output outside the checkout:

```powershell
dotnet build DrmcPatientPortal.slnx -c Release -warnaserror --artifacts-path ../_scratch/build-artifacts
dotnet publish src/DrmcPatientPortal/DrmcPatientPortal.csproj -c Release -o ../_scratch/publish --artifacts-path ../_scratch/build-artifacts -warnaserror
```

Copy the complete publish output to the dedicated IIS publish directory. The generated `web.config` uses **InProcess** hosting. Configure application settings at the dedicated pool rather than adding secrets to `web.config` or tracked JSON. `run.ps1` is a local `dotnet run` convenience script; IIS runs the published application.

## Production configuration

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
| `Notifications__Email__Host`, `Notifications__Email__Username`, `Notifications__Email__Password`, `Notifications__Email__FromAddress` | Required authenticated SMTP connection and sender. |
| `Notifications__Email__Port`, `Notifications__Email__UseSsl`, `Notifications__Email__Provider`, `Notifications__Email__FromName` | SMTP transport and sender presentation. Production requires SMTP with STARTTLS; Console delivery is rejected. |
| `Notifications__Sms__Enabled`, `Notifications__Sms__Endpoint`, `Notifications__Sms__ApiKey`, `Notifications__Sms__SenderId` | Optional HTTPS SMS webhook delivery. |
| `AdminBootstrap__Enabled`, `AdminBootstrap__Email` | Temporary first-Admin signup for one bare email address. |
| `PatientResults__ShowFullResults` | Institution-approved disclosure of full laboratory/radiology PDFs; disabled by default. |
| `Proxy__KnownProxies__N` | Explicit trusted proxy IP addresses when a reverse proxy is present. |
| `Ocr__NativeLibraryPath`, `Ocr__TessDataPath` | Optional overrides for bundled native OCR libraries and language data. |

Use separate absolute mutable roots outside publish. Define their paths explicitly in the settings file so offline maintenance uses the same paths. The helper can also create non-overlapping child directories beneath `StorageRoot` for omitted storage settings; if using those defaults, resolve and reuse the resulting pool settings for maintenance. Retain those paths on subsequent deployments. Certificate/password files must also be outside `StorageRoot` and every mutable directory. The HTTPS certificate selected by `CertificateThumbprint` and the RSA key-protection certificate serve separate purposes.

Production rejects missing required configuration, unsafe or overlapping storage paths, invalid notification configuration and development fixtures. The helper treats `SettingsFile` as desired configuration: omitted stale application-owned settings are removed, while unrelated variables and existing storage/protection bindings are preserved. Include every intended nondefault setting. Indexed certificate/proxy settings support indices 0 through 31.

## Configure IIS

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

## Prepare schema and create the first Admin

Follow this order for installation or upgrade:

1. For an existing installation, prevent new imports, drain active workers and cancel queued/review-pending imports created before validation **3.0.0**. Retain original files for re-upload.
2. Stop every portal instance. Back up the database, private files, full key ring including `redstar-blind-index-v1.protected`, certificate material and configuration as one consistent set. For a new installation, create an empty database with `Latin1_General_100_BIN2` collation.
3. Publish/copy the application and configure IIS as above. Preserve existing external paths, or complete the planned offline file/key transfer. Leave the pool stopped.
4. In a restricted maintenance process, load the **same Production, storage, key and certificate settings** and the maintenance SQL connection. From the publish directory run:

   ```powershell
   dotnet DrmcPatientPortal.dll redstar-protection upgrade
   ```

   This command applies migrations and initializes/verifies protection. It is safe to rerun after success or interruption. Normal Production web startup verifies preparation and does **not** apply migrations. Return to the intended runtime SQL identity before starting IIS.
5. For the first Admin only, configure `AdminBootstrap__Enabled` and `AdminBootstrap__Email` in the settings file, apply the helper, then start the dedicated pool. Verify `/health/ready`. Register that email through the portal, follow the delivered confirmation link and enroll an authenticator. Restart the pool to promote the fully enrolled account. Incomplete enrollment survives restart; an existing Admin prevents promotion of another configured account.
6. Disable bootstrap in the settings file, apply the helper and start the pool again. Sign out and complete a fresh password-plus-authenticator login. Verify Admin access and readiness. Existing installations use their existing Admin. Re-upload canceled imports under validation 3.0.0.

Admin creates email-bound invitations for scoped staff roles. Each invited user must confirm email, enroll MFA and complete a fresh MFA login before staff access activates. Patient accounts link through issued registration codes and matching hospital identity details. Red Star account linking and hospital mapping require its separately authorized clinic role.

## Operations and release checks

Verify `/health/live` and `/health/ready`, including readiness after the two-minute worker startup grace period. Exercise account email delivery, role-specific workflows, report access, OCR, restart persistence and permission denials with synthetic data before using real records. Readiness alone does not prove external email delivery.

Retain the database and all protected storage/key/certificate material together. Replacing a checkout or publish directory does not reset accounts or encrypted storage. Rollback requires restoring the matched backup set. Current-user DPAPI requires its original Windows identity/profile; migrate its protection deliberately under that identity before changing identities. Rehearse coordinated backup restoration before launch.

Real IIS authentication, bindings, certificate trust, ACLs, profile loading, preload and recycle behavior require validation on the target server; local HTTPS Kestrel checks do not establish them. External SMTP/SMS delivery, backup restoration and hospital concurrency remain **UNVERIFIED** until separately exercised. Prior readiness work used planning limits of 50,000 patients and 5,000 Red Star records; these are not enforced capacity guarantees or evidence of hospital-scale performance. Staff/Admin interfaces are English; public/patient interfaces support English, Filipino and Cebuano.
