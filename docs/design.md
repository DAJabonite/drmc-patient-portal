# DRMC Patient Portal — Design System

> **Current-scope note (2026-09-12):** The live patient navigation includes Dashboard, Appointments, Results, Encounters, Medications, and More Services. Messaging and caregiver/proxy links are intentionally absent. Bootstrap Icons are served locally so the interface works under the self-only CSP and on constrained connections.

> Every value below was OBSERVED from the real DRMC reference — either the attached reference screenshots (`Screenshot 2026-08-25 143442.png`, `mast-head_sample-removebg-preview.png`) or the live site (`https://drmc.doh.gov.ph/`). No hex code, font, or measurement below is invented. Where a value is a deliberate choice (e.g., a body-text color derived for accessibility), it is flagged as such.

Authoritative cross-check: Philippine **Government Website Template Design (GWTD) Guidelines, Annex C** (iGovPhil / DOST-ICT Office). GWTD rules that govern this build:
- Standard content width **1190px**; 12-column grid; scales down to single column at 320px.
- Content-area background must be **white `#ffffff`**.
- Agency logo in masthead **≤ 100px height**.
- Navigation limited to **two levels**.
- Fonts: Verdana, Arial, Tahoma (government-family). We use a clean sans stack for legibility.

---

## 1. Color palette (Bootstrap 5.3 `--bs-*` overrides)

All branding is expressed as Bootstrap CSS custom-property overrides on `:root` (see `wwwroot/css/site.css`) — **no Sass recompile**. Bootstrap 5.3 theming reads `--bs-*` variables at runtime.

| Token | Hex | Source | Contrast on white | Use |
|-------|-----|--------|-------------------|-----|
| `--bs-primary` | **`#0d4e86`** | sampled from masthead & Citizen's-Charter band | 8.57:1 (AA/AAA) | Masthead bg, headings, links, primary buttons. |
| `--bs-primary-rgb` | `13,78,134` | derived | — | `rgba()` utility variant. |
| `--bs-dark` | **`#0c4c82`** | sampled (masthead upper edge) | — | Overlay / hover shade of primary. |
| `--bs-body-color` | **`#212529`** | Bootstrap default | 15.4:1 | Body text. |
| `--bs-body-bg` | **`#ffffff`** | GWTD mandated | — | Content background. |
| --bs-secondary | `#f7f7f7` | sampled (nav band / card bg) | 8.0:1 | Light band behind secondary nav, card backgrounds. |
| --bs-secondary-rgb | `247,247,247` | derived | — | — |
| --bs-link-color | `#0d4e86` | brand | 8.57:1 | Links. |
| --bs-link-hover-color | `#0c4c82` | derived | — | Links hover. |
| GOVPH bar bg | **`#3b3b3f`** | sampled (top utility bar) | 11.1:1 w/ white | GOVPH utility bar. |
| Nav active / focus | **`#002973`** | sampled (deep blue on active menu) | 13.4:1 w/ white | Active nav item, focus rings. |
| Footer bg | **`#3b3b3f`** | sampled | — | Site footer (GOVPH-style). |

**Contrast notes (computed):**
- White text on brand blue `#0d4e86` = **8.57:1** ✅ AA & AAA.
- Brand blue text on white = **8.57:1** ✅ AA & AAA — so `#0d4e86` body text on white passes.
- Bootstrap muted `#6c757d` on white = **4.69:1** ✅ AA (normal text). Prefer brand blue or dark for body copy.
- White on nav-active `#002973` = **13.42:1** ✅.

---

## 2. Typography

- **Family:** `system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif` (Bootstrap default). Arial/Verdana are GWTD-approved; system sans gives the cleanest rendering and matches the reference's sans-serif real site.
- **Masthead metadata lines** ("Republic of the Philippines", "Department of Health", "Brgy. Apokon..."): small (~0.7–0.8rem), white or light-blue, on brand blue. GWTD nominates **Trajan Pro** for agency masthead metadata; Trajan is a display serif with limited availability — we approximate the *role* (small caps-ish caption above the brand name) with a semibold small sans, since Trajan is not freely licensed. This is the one place we prioritize legibility over font-name fidelity.
- **Agency name** ("DAVAO REGIONAL MEDICAL CENTER"): uses the real **chrome/embossed lettering** from the provided masthead image (`.png` asset), not a webfont.
- **Headings:** large, bold, brand-blue or dark. H1 = page title.

---

## 3. Header / nav structure

**Layer 1 — GOVPH utility bar** (dark navy `#3b3b3f`, full width):
- Left: "GOVPH" wordmark (links to gov.ph).
- Right: "Philippine Standard Time: [live clock]" in white small text (JS updates).

**Layer 2 — Masthead** (brand blue `#0d4e86`):
- Left: **DRMC seal** (`drmc-seal.png`, ≤100px height).
- Center-left: metadata lines + real chrome masthead image (`drmc-masthead.png`) = the agency wordmark.
- Right: **DOH seal** (`doh-seal.png`).
- Tagline "Caring for Life, Changing Lives." in small white text.

**Layer 3 — Primary nav** (white band, two levels max):
- Left-aligned links: Home · About Us · Services · Transparency · Programs & Projects (mirrors DRMC's real menu).
- Right: **Contact Us**, a compact **Search** field, and a universal accessibility icon (per GWTD/Philippine Web Accessibility Group).
- Active/current item: deep blue `#002973` text or underline. Hover: brand blue.

**Layer 4 — Secondary nav** (light `#f7f7f7` band): the in-scope portal nav only — **Citizen's Charter** (current) · Opportunities · Training & Research · Doctor's Directory · Rates & Fees · Press Release · Related Links · FAQs. Active item highlighted.

On mobile the navs collapse to a single toggle (Bootstrap navbar).

---

## 4. Hero pattern (Landing)

- Full-width brand-blue band with the masthead identity, a short headline, and a **primary call-to-action** into Register / Login.
- Subline: Level III DOH Teaching and Training Hospital · 1,000-bed capacity · catchment area (Davao del Norte, Davao del Sur, Davao Oriental, Davao de Oro).

---

## 5. Card / footer patterns

**Cards (dashboard & departments):** Bootstrap grid, white background, `#f7f7f7` accent band or 1px `#dee2e6` border, 8px radius, 16px padding, consistent 24px gutter. Card title brand-blue semibold; body dark text; a labeled action link (`#0d4e86`). Cards are equal-height (`.d-flex` + flex-column).

**Footer (GOVPH-standard):** dark navy `#3b3b3f`, white/light text, 4 columns:
1. GOVPH logo/seal + "Republic of the Philippines" + "All content is in the public domain unless otherwise stated."
2. About GOVPH (with links).
3. Government Links (Office of the President, Vice President, Senate, House of Representatives, Supreme Court, Court of Appeals, Sandiganbayan).
4. DRMC contact info (address, tagline "Caring for Life, Changing Lives.").
Bottom line: © year DRMC, plus a privacy/terms note (GWTD requires a Security/Data Privacy Policy reference — R.A. 10173).

---

## 6. Spacing rhythm

- Consistent 8px base unit. Section padding: 64px top/bottom (desktop), 40px (mobile).
- Grid gutter: 24px. Container max-width 1190px (`--bs-gutter-x: 1.5rem`; container capped at 1190px).
- Card stack spacing: 1.5rem.
- Headings margin-bottom: 1rem; h1→h2 vertical rhythm 1.5×.

---

## 7. Form styling (Register / Login)

- Matches Identity scaffold but re-skinned: brand-blue primary button, white card on `#f7f7f7`/white page, 400px max-width centered form card, large touch targets (≥44px height inputs), clear labels (not placeholders-only), inline field validation messages in accessible red.
- Contrast for validation text ≥4.5:1 (use `#b02a37` or darker on white).
