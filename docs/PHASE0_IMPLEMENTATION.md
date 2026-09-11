# Phase 0 — Governance and Safety Rails

## Delivered in this change

- Configuration-backed feature approval registry with fail-closed Production behavior.
- Explicit repository-owner approval for preserving the existing Malasakit calculator.
- Shared source-state model that distinguishes available, confirmed empty, stale, unavailable, offline, and error conditions.
- No-store/no-cache response controls for authenticated requests and protected patient/account-management routes.
- Unit tests for approval gates, source states, and protected-route classification.
- Approval and evidence register for product and institutional decisions.

## Deliberately unchanged

- Malasakit assessment, matching, checklist, and estimated coverage behavior.
- Registration lifecycle, password policy, patient clinical displays, queue lookup, medication refill, and proxy access. These are handled in later focused phases so changes remain reviewable.

## Next implementation slice

Phase 1A should introduce the account lifecycle and schema only:

1. `PendingVerification`, `CorrectionRequired`, `Active`, `Rejected`, and `Suspended` states;
2. non-predictable registration references;
3. privacy-notice acknowledgment separate from optional consent; and
4. migrations and lifecycle unit tests.

UI and staff verification actions should follow after the lifecycle model is stable.
