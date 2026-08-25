# Critique Rubric — DRMC Patient Portal

This is the adapter of the adversarial `/critique` design-critic pattern (per plugin87/ux-ui-agent-skills) plus a supplementary CRAP / HCI / task-first UX checklist (per oiloil-ui-ux-guide entry in BehiSecc/awesome-claude-skills) and the Philippine Government Website Template Design (GWTD) accessibility baseline.

**The critic never grades its own homework.** When switching into Critic Mode you must:
1. Render the actual page (run the app, screenshot at ~1280px and ~390px).
2. Review ONLY the fresh screenshot + `docs/design.md` + the reference images.
3. Never justify a gap from memory. If you catch yourself explaining why a design decision was made instead of naming the defect — you've slipped into Builder Mode. Say so and restart the critique.

---

## 1. Severity levels (every finding gets exactly one)

| Level | Meaning | Gate |
|-------|---------|------|
| **Critical** | Breaks the core flow (register, login, navigate to dashboard); renders broken/illegible; gross brand misrepresentation (wrong logo placement, invented colors/fonts that contradict references); fails to gate `[Authorize]`. | MUST fix before converging. |
| **Major** | Clearly wrong against `docs/design.md` or the reference screens (spacing rhythm off, header/footer drift, contrast < AA, oversized/undersized type, misaligned grid, unresponsive at 390px, card hierarchy confusing). | MUST fix before converging. |
| **Minor** | Polish/Optimization: slightly off spacing, an awkward line-wrap, a less-than-ideal focal point. | May remain at convergence. |
| **Enhancement** | A "would be nice" improvement not required by spec or references. | May remain at convergence. |

**Convergence = no Critical, no Major.** Two consecutive rounds with no visible improvement = plateau → stop the piece.

---

## 2. Method — how to critique

1. **Render, don't read source.** Judge the rendered output taken from a running app at ~1280px and ~390px. Static `.cshtml` source is never acceptable evidence of good design; if you cannot render (no headless browser available), say so explicitly in `GAUNTLET_STATE.md` and fall back to the best available rendered HTML, flagged as such.
2. **Click every interactive control** that exists in scope: Register link, Login link, every dashboard card, every header/footer link.
3. **Findings cite visible evidence.** Each finding must reference what you actually see (e.g., "the masthead blue is #1a2b3c but design.md says #0d4e86"; "the card grid collapses to 1 column at 390px leaving a 4:1 slug of whitespace").
4. **A passing automated check is not proof** the design is good. Accessibility scores, Lighthouse green, or a valid HTML validator do not substitute for judging the actual visual design.
5. **End with ONE biggest gap.** Not a laundry list. Name the single most consequential next fix.

---

## 3. WCAG 2.2 AA baseline (fold into every review)

- **Contrast:** text vs. background ≥ 4.5:1 (normal), ≥ 3:1 (large text ≥24px/18.66px bold, and for UI components/graphical objects). White text on `#0d4e86` must pass (compute and note the ratio). Blue `#0d4e86` body text on white background — verify ≥4.5:1 (it is borderline; if it fails, darken the text color for body copy, keep `#0d4e86` for large headings only).
- **Target size:** interactive targets ≥ 24×24 CSS px (AA 2.2); aim for ≥44×44 where practical for older-phone users.
- **Keyboard & focus:** visible `:focus-visible` outline; full keyboard operability of nav, forms, and cards.
- **Focus not trapped.** Skip-to-content link (GWTD/Microsoft-friendly) present.
- **Language & semantics:** `lang="en"`; landmarks (`<header>`, `<nav>`, `<main>`, `<footer>`); heading hierarchy h1→h2→h3, no skipped levels.
- **Reflow:** no horizontal scroll at 320px width; content reflows to single column.
- **Responsive:** mobile-first, scales down to 320px per GWTD.

---

## 4. Layout & structure (GWTD + CRAP)

- Standard content width **1190px** (GWTD), 12-column grid; scales down to single column on smallest screens.
- Content-area background must be **white `#ffffff`**.
- Agency logo in masthead **≤100px height** (GWTD).
- Navigation limited to **two levels** (GWTD).
- **CRAP:** Contrast, Repetition, Alignment, Proximity — check each.
  - *Contrast:* does the primary action stand out?
  - *Repetition:* are header/nav/footer identical across all four screens; is spacing rhythm consistent?
  - *Alignment:* is the grid aligned; are cards on a consistent grid?
  - *Proximity:* are related elements grouped, unrelated elements separated?
- **Task-first UX:** the dominant task on each screen must be discoverable in the first viewport without scrolling (Landing: "create account / sign in"; Dashboard: the summary cards).
- **HCI laws:** Hick's law (limit choices on forms), Fitts's law (primary action large and reachable), law of proximity (group related information), serial-position (put the primary action and key info first/last).

---

## 5. Brand fidelity check (Critical unless noted)

- Masthead: DRMC seal **left**, DOH seal **right**, on brand blue `#0d4e86`, with chrome "DAVAO REGIONAL MEDICAL CENTER" lettering and the metadata lines ("Republic of the Philippines" / "Department of Health" / "Brgy. Apokon, Tagum City, Davao del Norte").
- GOVPH top utility bar (dark navy) with "GOVPH" + Philippine Standard Time clock.
- Tagline "Caring for Life, Changing Lives." present.
- No invented colors/fonts. Every token in `docs/design.md` must be observed, not imagined.
- NEVER use "Demo", "Prototype", "Sample", "Mock", or "Test" in patient-facing UI text. (Allowed in code comments, commits, README.)
