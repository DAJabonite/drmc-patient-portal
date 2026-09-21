# Security Review Register

Reconciled: 2026-09-22. This register distinguishes implemented application controls from external work requiring DRMC authority or credentials.

## Implemented application controls

| Control | Implementation | Verification |
|---|---|---|
| Account brute-force protection | Five failures cause a 15-minute lockout | Identity configuration and login handler |
| PHI auditing | Audit persistence exceptions propagate; clinical mutation transactions roll back; middleware returns localized 503 | Audit service and controller transaction paths |
| Document confidentiality | Data Protection encryption, server filenames, MIME metadata, canonical paths, content verification, 10 MB cap | Storage tests cover encryption, session binding, and spoof rejection |
| Temporary uploads | Protected 30-minute session token, encrypted staging, post-use removal, periodic cleanup | Storage service and hosted cleanup service |
| Legacy document migration | Encrypted copy written, metadata committed, then plaintext deleted | Controller/service ordering |
| Browser hardening | CSP `default-src 'self'`, `nosniff`, `SAMEORIGIN`, `no-referrer`, local icons | Middleware and layout |
| Notification boundaries | Console senders only in Development; validated SMTP and HTTPS JSON SMS adapters in Production | Environment-specific DI configuration |

## Open institutional work

| Priority | Item | Required owner / decision |
|---|---|---|
| Critical | Production Data Protection key encryption, custody, backup, rotation, and disaster-recovery exercise | DRMC infrastructure and security |
| Critical | DPIA, privacy notice, consent, retention, deletion, and ID-image legal basis | DRMC DPO and legal |
| Critical | Independent penetration test, threat model, SAST/DAST policy, remediation SLA | DRMC security |
| High | SMTP/SMS vendor review, credentials, sender IDs, delivery retry/dead-letter monitoring | DRMC infrastructure/communications |
| High | Centralized audit/SIEM export, immutable retention, alerting, privileged access | DRMC security |
| High | HIS and pharmacy interface authorization, message contracts, mutual authentication, idempotency, reconciliation | DRMC integration teams |
| High | Clinical governance for emergency triage, medication schedules, and patient-facing instructions | DRMC clinical safety |
| Medium | Formal WCAG 2.2 AA and EN/FIL/CEB native-speaker review | Accessibility and communications |
| Medium | Rate limiting/WAF policy for login, registration, uploads and record endpoints | Infrastructure/security |
| Medium | Backup restoration and encrypted-document/key-ring recovery runbook | Infrastructure |

## Removed features

Booking/check-in, refill submission, messaging and caregiver/proxy access are not active, are not routed, and are outside the current review surface. Historical plans describing those features are superseded.

## Operational rules

- Do not deploy with console notification senders.
- Do not place patient documents or Data Protection keys in a web-served directory.
- Do not claim HIS, clinician, pharmacy, or SIEM synchronization until an adapter confirms remote acceptance.
- Treat audit failures and localized 503 responses during PHI access as security/availability incidents.
- Retired appointment URLs return 404. Retained triage endpoints require the authenticated owner of the appointment.

See [deployment guidance](DEPLOYMENT.md) for required settings.
