# Approval and Evidence Register

This register records repository-owner and institutional decisions that affect FR implementation. Production gates fail closed when required evidence is missing.

| Decision ID | Area | Decision | Owner | Effective date | Implementation effect | Status |
|---|---|---|---|---|---|---|
| DEC-2026-09-11-MALASAKIT-001 | Malasakit | Preserve the existing Malasakit assessment flow, including eligibility guidance and estimated coverage calculation/promise behavior. | Repository owner | 2026-09-11 | `MalasakitCoverageCalculator` remains enabled. This is an explicit product-owner exception to the restrictive interpretation in FR-73, FR-78, and A-12/A-15. | Approved |

## Pending institutional evidence

The following remain disabled for Production until complete evidence is recorded:

- detailed laboratory findings and downloads;
- Digital ID verification;
- personal financial-assistance status integration;
- unapproved external integrations and source claims;
- PWA offline behavior involving anything beyond approved public content;
- production notification, retention, encryption-key, and staff-role policies.

## Evidence rule

An enabled Production feature must have all of the following:

1. a stable decision/evidence reference;
2. an identified approver or accountable owner;
3. an approval/effective timestamp; and
4. implementation notes defining the approved boundary.

Development seed data, placeholder adapters, existing screen copy, and guessed institutional policy do not count as approval evidence.
