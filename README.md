# DRMC Patient Portal

## Overview

The DRMC Patient Portal is a patient-facing ASP.NET Core application for Davao Regional Medical Center. It provides public hospital information, patient registration and sign-in, and access to locally stored patient records.

This repository is a development and review implementation. It is not connected to DRMC clinical, pharmacy, billing, social-work, or security systems. Seeded patients, records, schedules, notices, and guidance must be institutionally verified before production use.

## Current Features

- Public home page, doctor and department directory, OPD guide, advisories, privacy notice, terms, and official DRMC links.
- Malasakit service guides for DSWD, DRMC Malasakit, DOH-MAIFIP, and PhilHealth requirements.
- Patient registration with government-ID selection, optional local OCR or photo capture, manual entry, privacy consent, and residential address.
- Identity sign-in, lockout, profile management, authenticator-based two-factor authentication, and an owner-scoped encrypted ID wallet.
- Authenticated dashboard with visits and care notes, laboratory results, medications, dose schedules, allergy information, and patient access history.
- English, Filipino, and Cebuano shared navigation and guidance resources.

## Tech Stack

- .NET 10, ASP.NET Core MVC, Razor Pages, and ASP.NET Core Identity
- Entity Framework Core 10 with SQLite and versioned migrations
- ASP.NET Core Data Protection for document encryption and temporary upload tokens
- Tesseract for local ID OCR and QRCoder for authenticator enrollment
- Local Bootstrap, Bootstrap Icons, jQuery, and unobtrusive validation assets

## Project Structure

```text
src/DrmcPatientPortal/
  Areas/Identity/Pages/  Registration, sign-in, profile, ID wallet, and 2FA
  Controllers/           Public and authenticated MVC endpoints
  Data/                  DbContext, development initializer, and EF migrations
  Models/                Persisted entities, view inputs, and static catalogs
  Resources/             English, Filipino, and Cebuano shared strings
  Services/              Audit, encrypted documents, OCR, QR, and notifications
  Views/                 Razor views for current public and patient journeys
  wwwroot/               Local CSS, JavaScript, images, and vendor libraries
run.ps1                  Local development launcher
```

## Requirements

- .NET 10 SDK
- PowerShell when using `run.ps1`
- SQLite-compatible storage available to the application process
- Writable persistent directories for the database, Data Protection keys, and patient documents

## Setup

From the repository root:

```powershell
dotnet restore DrmcPatientPortal.slnx
dotnet build DrmcPatientPortal.slnx -c Release
```

Configuration defaults are in `src/DrmcPatientPortal/appsettings.json`. Use environment variables with double underscores for nested settings and a secret store for credentials.

| Setting | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | SQLite connection; the default is `DataSource=app.db;Cache=Shared` |
| `DataProtection__KeyRingPath` | Persistent encryption-key directory |
| `PatientDocuments__RootPath` | Encrypted patient ID document directory |
| `PatientDocuments__TemporaryPath` | Staged registration upload directory |
| `PatientDocuments__MaximumFileSizeMb` | Upload size limit; default 10 MB |
| `PatientDocuments__TemporaryFileLifetimeMinutes` | Temporary upload lifetime; default 30 minutes |
| `Notifications__Email__*` | SMTP host, port, TLS, credentials, and sender address |
| `Notifications__Sms__*` | HTTPS webhook endpoint, API key, and sender ID |

Relative storage paths resolve from the application content root. Keep databases, encryption keys, patient documents, and secrets outside Git and outside `wwwroot`.

## Run

```powershell
.\run.ps1
```

Or run the project directly:

```powershell
dotnet run --project src/DrmcPatientPortal --launch-profile http
```

Open `http://localhost:5095`.

Development applies migrations, seeds review data, and logs email and SMS deliveries to the console. Local review accounts are:

| Account | Password | Dataset |
|---|---|---|
| `patient@drmc.doh.gov.ph` | `P@tient2026` | Seeded visits, laboratory results, medications, and allergies |
| `juan@drmc.doh.gov.ph` | `J@uan2026` | Empty clinical record for empty-state review |

Never expose these accounts in a production installation.

## Database and Backend Reference

`Data/ApplicationDbContext.cs` defines the active model. The principal relationships are:

- `ApplicationUser` owns `PatientIdDocument` metadata; encrypted document files are stored outside SQLite.
- `ClinicalEncounter` belongs to a patient and represents OPD, emergency, or inpatient visits.
- `LabResult` belongs to a patient, can reference a `ClinicalEncounter`, and owns `LabResultItem` rows.
- `Prescription` belongs to a patient and owns `MedicationDoseSchedule` rows; `PatientAllergy` is patient-scoped.
- `AuditLog` records actual patient access and account activity.
- `Doctor` and `PublicAdvisory` provide database-backed public content; OPD and Malasakit guidance is application content.

The latest migration is `20260921170812_RemoveRetiredPortalFeatures`. Keep the complete migration history so existing databases can upgrade to the active schema.

Production does not run `DbInitializer` or apply migrations automatically. Back up the database, encryption keys, and patient documents, then apply migrations before starting a release:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.11
dotnet ef database update --project src/DrmcPatientPortal
```

## Important Development Notes

- `DbInitializer` is a development-only fixture loader. It does not reset existing passwords or patient-entered data.
- Outside Development, valid SMTP and HTTPS SMS settings are required at startup.
- Preserve owner-scoped queries, antiforgery validation, encrypted document storage, and audit logging when integrating real backends.
- Public advisory HTML requires a trusted publishing workflow.
- Clinical records and public guidance are local review data until authoritative DRMC integrations and institutional approval are in place.
