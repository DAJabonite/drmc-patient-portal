# Hospital record code for portal signup — implementation plan (gauntlet loop)

## Goal

A person who already has a DRMC hospital record (for example, someone who has been admitted or seen at a clinic) can get a **hospital record code** from the PACD (Public Assistance and Complaints Desk) or from a clinic desk such as the Red Star Clinic. They enter the code, or scan its QR, on the **Credentials** step of signup. The code proves they have a record at DRMC, and the new account is linked to that record as soon as it is created. No separate staff linking step is needed.

## Research summary

| Source | What we adopt |
| --- | --- |
| Epic MyChart activation codes (enrollment letter or After Visit Summary, 3 × 5 characters, not case-sensitive, checked together with date of birth) | 15-character code in three groups of five, not case-sensitive, always checked against the record's date of birth |
| NIST SP 800-63A §4.6 / §5.3.2 enrollment codes (random, at least as strong as six random alphanumerics, QR allowed, valid up to 7 days when given in person, reset on first use) | 75-bit random code, QR form, 7-day default lifetime (configurable 1–30 days), single use |
| OWASP Forgot Password cheat sheet (random from a CSPRNG, stored securely, single use, expiring, tied to one user, same response for every failure) | Store only a SHA-256 hash and a 5-character hint. One active code per record. One generic error for every failure |
| Existing portal rules (README: staff verify identity; never link automatically from names or birth dates) | Code is issued only after staff check the patient's ID in person and tick "Staff checked the patient's identity". Issuing is audited |

The QR encodes `https://<portal>/Identity/Account/Register#code=XXXXX-XXXXX-XXXXX`. The code is in the URL **fragment**, which browsers never send to the server, so it stays out of server logs and referrers. Scanning the QR with a phone camera opens signup with the code already filled in. On browsers that support `BarcodeDetector`, a **Scan QR code** button also reads the QR with the camera from within the form. Other browsers hide the button and the patient types the code.

## Scope

1. **Data**: a `PatientRegistrationCodes` table (record FK with cascade, unique hash, hint, issuing point, issuer, issued, expires, redeemed and revoked timestamps, rowversion) and one additive migration.
2. **Staff role**: `PatientServicesStaff` ("Patient services (PACD / clinic)"), granted under Staff access like LabStaff and RadiologyStaff. It can open the dashboard and Registration codes only.
3. **Admin › Registration codes**: list with status, patient picker (name and hospital number only), issue form (patient context plus date of birth to check, issuing point, identity checkbox), one-time printable slip with code and QR, and revoke. A **Registration code** button is added on the patient details page.
4. **Signup › Credentials**: an optional "Hospital record code" panel styled like the existing consent summary, with auto-formatting, QR scan, and a `#code=` prefill. A server-side check runs before the account is created. The code is redeemed and the record linked right after creation, and the account is rolled back on failure. The redemption is written to the patient activity log.
5. **Config**: `PatientRegistration:RequireHospitalRecordCode` (default `false`, so the current open signup is unchanged) and `PatientRegistration:CodeLifetimeDays` (default `7`).
6. **Docs and tests**: README sections, integration tests and screenshots.

Out of scope: rate limiting middleware (75-bit codes plus a date-of-birth match make guessing impractical), SMS or email delivery of codes, and Cebuano strings (the signup page is English-only today).

## Gauntlet loop

Each round must pass every gate in order. If any gate fails, fix the cause and **restart the round from Gate 1**. Advance only when a full pass is clean.

| Gate | Check | Pass condition |
| --- | --- | --- |
| G1 Build | `dotnet build -c Release -warnaserror` | 0 warnings, 0 errors (same as CI) |
| G2 Model | `dotnet ef migrations has-pending-model-changes` | No pending changes |
| G3 Regression | Full existing suite (74 tests before this change) | All green, no existing test edited except to add the new role or family |
| G4 Feature tests | New tests for issue, redeem, expiry, revoke, reuse, DOB mismatch, wrong code, required mode, access matrix, CSP | All green |
| G5 Adversarial | Replay a used code, try a revoked or expired code, try another person's DOB, use lower case or spacing variants, POST as Lab, Radiology or Patient, anonymous GET, check that no plaintext code is stored | Every case rejected or handled as designed |
| G6 UX and design | Screenshots at desktop and mobile widths. Check Bootstrap and DRMC tokens, focus order, labels, `aria-describedby`, keyboard-only use, no inline script (CSP) | Matches the existing Credentials step and Admin card patterns |
| G7 Non-destruction | `git diff --stat` reviewed; existing signup without a code still works; migration is additive only | No unrelated changes |

Rounds:

1. **Round 1, foundation**: model, migration, code service (generate, normalise, hash), unit-level tests (G1–G3).
2. **Round 2, staff side**: role, policy, controller, views, slip and QR, access matrix (G1–G5).
3. **Round 3, patient side**: signup panel, JS (format, scan, fragment prefill), server verification and redemption (G1–G5).
4. **Round 4, polish**: screenshots, README, design check (G1–G7). Then open the PR. **Do not merge.**
