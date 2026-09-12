# Deployment and Operations

## Database migrations

Back up the database, then apply migrations before the new application starts:

```powershell
dotnet ef database update --project src/DrmcPatientPortal/DrmcPatientPortal.csproj
```

`20260912091834_RestoreEncountersAndHardenPortal` is forward-only. SQLite rebuilds temporarily change `PRAGMA foreign_keys`; do not interrupt migration execution. Verify migration history and create a post-deployment backup.

## Data Protection and documents

Configure persistent, access-restricted paths:

- `DataProtection:KeyRingPath`
- `PatientDocuments:RootPath`
- `PatientDocuments:TemporaryPath`
- `PatientDocuments:MaximumFileSizeMb` (default 10)
- `PatientDocuments:TemporaryFileLifetimeMinutes` (default 30)

Protect and back up the key ring; losing it makes encrypted documents unreadable. Keep document paths outside `wwwroot` and restrict them to the application identity and approved backup operators.

Legacy plaintext IDs encrypt lazily on authorized access. Metadata commits before plaintext deletion. Apply approved retention and backup-erasure rules to legacy copies.

## Notifications

Development uses console senders. Production fails startup unless both providers validate.

SMTP: `Notifications:Email:Host`, `Port`, `FromAddress`, `Username`, `Password`, `UseSsl`.

SMS webhook: `Notifications:Sms:Endpoint`, `ApiKey`, `SenderId`. The endpoint receives `to`, `message`, and `senderId` JSON with bearer authorization.

Provide secrets through the deployment secret store or environment, never committed files.

## Platform controls

Terminate TLS at a trusted reverse proxy, forward scheme/host safely, and force HTTPS. Preserve HttpOnly secure cookies and the self-only CSP; allow extra origins only after review.

PHI access and clinical mutations fail closed when audit persistence fails. Treat 503 responses and audit-write exceptions as operational incidents. Connect a SIEM only after DRMC approves transport, access, retention, and incident procedures.

HIS, pharmacy, and SIEM adapters are absent because approved interfaces and credentials are unavailable. Leave them disabled. Never label local triage/refill state as synchronized, accepted, or dispensed without confirmation from an institutional adapter.

## Release checks

```powershell
dotnet build DrmcPatientPortal.slnx --no-restore
dotnet test DrmcPatientPortal.slnx --no-build
dotnet list DrmcPatientPortal.slnx package --vulnerable --include-transitive
```

Smoke-test capability exchange and revocation, Encounters, ID retrieval, triage, refills, queue polling, languages, and error telemetry before traffic is enabled.
