# Compliance Readiness Report

Reconciled: 2026-09-22.

This is an engineering readiness report, not legal, clinical, privacy, accessibility, or institutional certification. Final approval belongs to DRMC's Data Protection Officer, legal counsel, clinical safety leadership, infrastructure team, and authorized security assessors.

## Implemented technical controls

| Domain | Evidence | Readiness |
|---|---|---|
| Authentication | ASP.NET Core Identity, 15-minute lockout after five failures, TOTP 2FA support, secure cookie defaults | Implemented; operational policy review required |
| PHI authorization | Encounter, lab, medication, triage, and ID-document queries are patient-owner scoped | Implemented and tested |
| PHI audit | Selected record access/mutation audit writes fail closed; generic localized retry page on audit failure | Implemented; SIEM integration pending |
| ID images | Verified JPEG/PNG, 10 MB cap, malformed/spoof rejection, encryption at rest, canonical paths, signed session tokens, cleanup, legacy migration | Implemented; production key management and retention approval required |
| Data minimization | Authenticator QR is generated locally; audit descriptions avoid unnecessary clinical content | Implemented |
| Web controls | CSP, `nosniff`, frame restriction, referrer restriction, local icon assets | Implemented; independent penetration test pending |
| Localization | EN/FIL/CEB shared resources for shell and restored workflows; clinical and patient-authored content stays verbatim | Implemented for current shared-resource surface; native-speaker review required |
| Accessibility | Semantic landmarks, keyboard focus, accessible status messaging, responsive Bootstrap layout | Engineering checks implemented; formal WCAG audit pending |

## Product-scope corrections

- Messaging is removed and is not an active communication channel.
- Caregiver/proxy access is removed and is not an active authorization model.
- Encounter summaries are restored with authenticated ownership and audit controls.
- Booking/check-in and refill submission are retired. Historical schema remains; there is no dispensing workflow.
- Triage and subsidy application routes remain available by direct URL but are not linked from primary navigation. Triage is saved locally and awaits clinical review.

## External approvals and dependencies

The repository cannot complete these without DRMC decisions or credentials:

1. DPIA, lawful-basis, consent-copy, retention, and data-subject-rights approval.
2. Clinical safety review of triage wording, emergency escalation, medication display, and encounter content.
3. Production hosting, TLS, backups, encrypted Data Protection key custody, recovery testing, and access logging.
4. SMTP/SMS vendor agreements, sender identity, delivery monitoring, and secret rotation.
5. HIS/pharmacy/SIEM interface contracts, mutual authentication, reconciliation, and incident response.
6. Independent penetration test and formal WCAG 2.2 AA evaluation with assistive technology.

## Verification

Run `pwsh scripts/test-ui.ps1` for current backend/browser, migration and accessibility results, and the NuGet vulnerability command in README for current package advisories. Dated reports record historical checks, not certification or continuing assurance.

See [STATUS.md](../STATUS.md), [deployment guidance](DEPLOYMENT.md), and the [security review register](SECURITY_REVIEW_TODO.md).
