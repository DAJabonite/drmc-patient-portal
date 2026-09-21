# Implementation status

Reconciled against the running application on September 22, 2026.

The [README](README.md) is the current setup, feature, route and backend-handoff reference. Running behavior and source code take precedence over dated reports.

- Main patient destinations: Home, Visits, Labs, Medication and Malasakit.
- Visits uses `Views/Visits`, `VisitsController` and `/Patient/Visits`; `/Patient/Encounters` URLs remain compatibility aliases.
- Malasakit's visible hub contains four document/service guides. Subsidy applications, medical history, program details and appointment-linked triage remain available by direct URL, with no primary-navigation entry.
- Booking/check-in, online queues, refill submission, messaging, proxy access and biomarker trends are inactive. Appointment/triage and refill schema history is retained for compatibility.
- DbInitializer supplies Development fixtures only. It creates triage dependencies on the first startup and preserves existing records on repeat runs. Audit entries come from actual actions; new fixtures do not claim pharmacy pickup approval or hospital synchronization.
- Latest migration: `20260916095804_RemoveOpdQueue`. This maintenance work changes no schema.
- Production integration, notification credentials, verified hospital content and institutional approval remain outstanding. See [deployment requirements](docs/DEPLOYMENT.md).

Run `pwsh scripts/test-ui.ps1` for current build and backend/browser results. Generated validation output is ignored under `output/playwright`; historical counts in dated documents are not a substitute for rerunning the suite.
