# Current Implementation Status

Last reconciled: 2026-09-16. This is the source of truth for current code; historical plans and logs are not current product scope.

## Implemented

- Official DRMC ARTA and Citizen's Charter landing-page references, verified live on September 13.
- Any government-issued ID registration, including a named Other option, Senior Citizen, and PWD IDs.
- Audited medical history grouped into OPD, ER, and Admitted, outside the five primary destinations.
- Persisted Malasakit applications and preparation checklists with separate reported and confirmed payment/eligibility/coverage states.

- Authenticated dashboard, lab results, restored encounter summaries, medications, triage, and audit history.
- Encounter ownership, linked labs, development seeds, and view audit events.
- Refill requests start `Requested`; Requested/Approved/Ready-for-Pickup block duplicates; refill counts do not change before dispensing.
- Persisted medication dose schedules and an explicit no-exact-schedule state.
- Persistent emergency triage, range validation, duplicate prevention, and fail-closed auditing.
- Encrypted ID storage, verified images, signed session tokens, cleanup, and metadata-first legacy migration.
- Identity lockout, security headers, secure cookies, local Bootstrap Icons, and validated Production notification adapters.
- English, Filipino, and Cebuano shared resources for navigation and restored clinical flows; clinical content stays verbatim.

## Deliberately not implemented

- Online appointment booking and self-service check-in workflows are retired and backburned per client directive. Outpatient visits follow the physical, on-site OPD Guide.
- Online OPD queue tracking is removed because DRMC does not provide a queue-tracking source of truth. The OPD guide still describes physical, on-site queue steps where applicable.
- Messaging and caregiver/proxy access remain removed.
- HIS, pharmacy, and SIEM connectivity require DRMC interfaces and credentials. No simulated synchronization is shown.
- Refill dispensing/inventory changes require a pharmacy adapter or staff workflow.
- Compliance certification requires DRMC DPO, legal, clinical safety, infrastructure, and penetration-test sign-off.

## Restored routes

| Method | Route | Authorization |
|---|---|---|
| GET | `/Patient/Encounters` | Authenticated patient |
| GET | `/Patient/Encounters/Details/{id}` | Authenticated owner |

Latest migration: `20260916095804_RemoveOpdQueue`.

See [September 13 client requirements](docs/CLIENT_REQUIREMENTS_2026-09-13.md) for routes, verification, and the outstanding institutional billing/social-work integration.

## Verification

Current client update: **34 backend and 46 browser tests pass**, including database upgrade and automated accessibility. The table below records the previous baseline; details are in [the requirements report](docs/CLIENT_REQUIREMENTS_2026-09-13.md).

| Check | Result |
|---|---|
| .NET 10 build | Pass; 0 warnings, 0 errors |
| Automated tests | 25 passed, 0 failed, 0 skipped |
| Fresh migration chain | Pass |
| Existing database upgrade | Pass |
| NuGet vulnerability audit | No known vulnerable packages |
| Browser | Encounters nav/details/labs/audit, desktop/mobile layout, and local icons verified |

See [deployment requirements](docs/DEPLOYMENT.md).
