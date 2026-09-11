# Phase 1A — Account Lifecycle Foundation

## Logic review

- Existing accounts default to `Active` so this migration does not lock out current users.
- New registration flows can be initialized as `PendingVerification` with a cryptographically random, non-predictable reference.
- Only `Active` accounts report eligibility for protected patient data.
- Allowed transitions are explicit; rejected accounts cannot be directly reactivated.
- Privacy-notice acknowledgment is mandatory and versioned.
- Optional consent is stored separately, remains optional, and requires a specific purpose when granted.

## Flow review

This slice deliberately introduces the domain model before redirecting real registrations. The existing registration and sign-in flow remains unchanged until Phase 1B adds the pending-status screen and active-account authorization policy in the same reviewable change. This avoids creating pending accounts that have no safe status destination.

Planned Phase 1B flow:

1. Submit registration.
2. Create account as `PendingVerification`.
3. Generate the opaque registration reference.
4. Persist separate privacy choices.
5. Do not sign the patient into protected records.
6. Redirect to a limited registration acknowledgment/status page.
7. Permit protected records only after an authorized transition to `Active`.

## UI review

No UI is changed in Phase 1A. The existing four-step registration interface therefore has no visual regression from this slice. Phase 1B will revise Step 4 so the privacy notice acknowledgment and optional purpose-specific consent are visibly separate, and will add the pending-status confirmation screen.

## Persistence

The migration backfills existing accounts as `Active`, adds lifecycle/privacy columns, and creates a unique nullable registration-reference index. SQLite permits multiple null values in a unique index, while assigned references must be unique.

## Validation

Unit tests cover backward compatibility, opaque-reference format and collision sampling, protected-data eligibility, permitted/forbidden transitions, and separation of mandatory notice acknowledgment from optional consent.
