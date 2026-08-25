# GAUNTLET STATE — DRMC Patient Portal

> Re-read this file at the start of every phase and every loop round. It is the single source of truth for this session. If context has drifted or been compacted, this file is your memory.

## Purpose
Patient-facing informational website with account access for **Davao Regional Medical Center (DRMC)**, a Level III DOH teaching & training government hospital in Tagum City, Davao del Norte, Philippines.

Tagline (real): **"Caring for Life, Changing Lives."**

---

## Stack decisions (verifiably installed / used)

| Item | Value | Note |
|------|-------|------|
| .NET SDK | **10.0.400** | Installed via `dotnet-install.sh` to `/home/user/.dotnet`; target `net10.0`. ASP.NET Core runtime 10.0.11. |
| EF Core | 10.0.11 | SQLite provider. |
| Identity | `Microsoft.AspNetCore.Identity.UI` 10.0.11 | Razor Pages scaffolded under `Areas/Identity/Pages/Account/`. |
| Bootstrap | **5.3.8** | Bumped from template's 5.3.3; theming via `--bs-*` CSS custom properties. |
| Data store | SQLite (`DataSource=app.db`) | Zero external DB setup; grader can `dotnet run` directly. |
| SDK path | `/home/user/.dotnet` | Source `.dotnet-env.sh` in each shell. |

**Run command:**
```
source .dotnet-env.sh
cd src/DrmcPatientPortal
dotnet run
```

---

## Brand/design tokens (extracted from real DRMC reference screenshots — see docs/design.md)

- Primary brand blue (GOVPH masthead/citizen's-charter band): **`#0d4e86`**
- Darker blue shade (hover/overlay): `#0c4c82`, `#0d4d85`
- GOVPH utility bar: dark navy `#3b3b3f` (with white text)
- Nav active-state blue: `#002973` (deep blue, matches Government template active menu)
- Content area background: **white `#ffffff`** (GWTD mandated)
- Nav band / card light gray: `#f7f7f7`
- Footer: dark navy with GOVPH seal + "Republic of the Philippines" + "All content is in the public domain" + Government Links.

---

## Scope (Phase 2 — hard, do not expand)

**In scope:** Landing (public) · Register · Login · Home/Dashboard (authenticated). Role: patient only.

**Out of scope (do NOT build, even partially — no stub controllers):**
appointment booking, lab results (beyond display card), secure messaging, medical records, telehealth, bilingual toggle, family-proxy access, **billing** (public hospital — no billing anywhere).

Dashboard cards for out-of-scope modules are **display-only**: no broken links, no fake "coming soon" pages. Either non-clickable or link to a real built page (e.g., Landing's Departments section).

---

## Critic rubric summary (filled after Phase 1 — see docs/critique-rubric.md)

Severity levels: **Critical** / **Major** / **Minor** / **Enhancement**. A passing automated check is NOT proof of design quality. Every finding must cite specific visible evidence from a fresh rendered screenshot.

**Stopping condition per piece:** no Critical or Major gap remains (Minor/Enhancement-only accepted as converged), OR two consecutive rounds show no visible improvement (plateau) — whichever first. Round count is NOT a quality bar.

---

## Safety cap (crash-guard only)

15 rounds per piece is a crash-guard, not a quality bar. If a piece hits it, mark it `CAPPED — NEEDS HUMAN REVIEW` in the round log and move on. Never report a capped piece as converged.

---

## Round log

Format: `piece | round | verdict | biggest gap named | status`

| piece | round | verdict | biggest gap named | status |
|-------|-------|---------|-------------------|--------|
| (phase 0 scaffold prep) | 0 | n/a | n/a | DONE |
| (phase 1 reference material) | 0 | n/a | n/a | DONE (docs/critique-rubric.md) |
| (phase 2 product spec) | 0 | n/a | n/a | DONE (embedded) |
| (phase 3 design system) | 0 | n/a | n/a | DONE (docs/design.md, docs/ux-notes.md, real seals/masthead extracted; departments verified against live site) |
| Landing | 1 | FAIL | Masthead duplicated: real lockup already contains seals/metadata; page showed doubled logo + floating seal (Critical) | FIXED |
| Landing | 2 | FAIL | Mobile GOVPH clock overflow + secondary-nav wrapping (Major) | FIXED |
| Landing | 3 | PASS | No Critical/Major; header reflows cleanly on mobile | CONVERGED |
| Register | 1 | PASS | Clean form, all profile fields, e2e register->dashboard verified | CONVERGED |
| Login | 1 | PASS | Clean form, invalid-login error accessible, e2e login->dashboard verified | CONVERGED |
| Dashboard | 1 | FAIL | "Clinical Departments" card linked to dashboard itself; Identity manage page unthemed (stock blue) (Major) | FIXED |
| Dashboard | 2 | PASS | Departments card -> /#departments; manage page branded; all 6 cards seeded | CONVERGED |
| Smoothing | 1 | PASS | Header/masthead/footer identical across all four screens (only auth-state nav differs, expected) | CONVERGED |
| Integration | 1 | PASS | 10/10 e2e checks pass (auth gate, register/login->dashboard, seed data, no forbidden UI words) | CONVERGED |

## Final convergence note

All four pieces (Landing, Register, Login, Home/Dashboard) individually converged with no Critical or Major gaps. One smoothing pass confirmed cross-page consistency. Full integration verified. No piece was capped. Final status: **CONVERGED**.

---

## Reviewer test credentials (seeded in DbInitializer — README only, never surface in UI)

Seeded accounts are created via `DbInitializer` at startup. Full credentials documented in README.md.
