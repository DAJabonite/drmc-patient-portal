# Current Implementation Status

Last reconciled: 2026-09-12. This is the source of truth for current code; historical plans and logs are not current product scope.

## Implemented

- Public booking with server-side department, type, doctor, doctor/department, slot, and conflict validation.
- Capability-protected appointment confirmation, check-in, and cancellation; hashed tokens expire 24 hours after the visit and revoke on cancellation.
- Authenticated dashboard, lab results, restored encounter summaries, medications, triage, and audit history.
- Encounter ownership, linked labs, printable views, development seeds, and view/print audit events.
- Refill requests start `Requested`; Requested/Approved/Ready-for-Pickup block duplicates; refill counts do not change before dispensing.
- Persisted medication dose schedules and an explicit no-exact-schedule state.
- Persistent emergency triage, range validation, duplicate prevention, and fail-closed auditing.
- Full queue-card refresh, accessible status, manual refresh, 30-second polling, and visibility throttling.
- Encrypted ID storage, verified images, signed session tokens, cleanup, and metadata-first legacy migration.
- Identity lockout, security headers, secure cookies, local Bootstrap Icons, and validated Production notification adapters.
- English, Filipino, and Cebuano shared resources for navigation and restored clinical flows; clinical content stays verbatim.

## Deliberately not implemented

- Messaging and caregiver/proxy access remain removed.
- HIS, pharmacy, and SIEM connectivity require DRMC interfaces and credentials. No simulated synchronization is shown.
- Refill dispensing/inventory changes require a pharmacy adapter or staff workflow.
- Compliance certification requires DRMC DPO, legal, clinical safety, infrastructure, and penetration-test sign-off.

## Restored routes

| Method | Route | Authorization |
|---|---|---|
| GET | `/Patient/Encounters` | Authenticated patient |
| GET | `/Patient/Encounters/Details/{id}` | Authenticated owner |
| GET | `/Patient/Encounters/Print/{id}` | Authenticated owner |

Latest migration: `20260912091834_RestoreEncountersAndHardenPortal`.

## Verification

| Check | Result |
|---|---|
| .NET 10 build | Pass; 0 warnings, 0 errors |
| Automated tests | 25 passed, 0 failed, 0 skipped |
| Fresh migration chain | Pass |
| Existing database upgrade | Pass |
| NuGet vulnerability audit | No known vulnerable packages |
| Browser | Encounters nav/details/labs/audit, desktop/mobile layout, and local icons verified |

See [deployment requirements](docs/DEPLOYMENT.md).
