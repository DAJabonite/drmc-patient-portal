# UX & Interaction Notes — DRMC Patient Portal

These notes cover **how** the register/login/dashboard flow should *behave*. They deliberately say nothing about branding — the branding always comes from `docs/design.md` and the real DRMC identity. These patterns are adapted from local and same-stack precedents:

- **PGH OCRA** (`pghopd.up.edu.ph`) — Philippine General Hospital's public outpatient login/appointment system; the closest local public-hospital precedent. Studied for *flow*, not visuals.
- **OpenEMR portal module** (`github.com/openemr/openemr`, `/portal`) — general patient-portal information architecture.
- **`AleGxrcia/Patient-Manager`** (ASP.NET Core MVC) and **`alexandra-valkova/DoctorAppointment`** (ASP.NET Core, Razor Pages, Identity, EF Core) — same-stack references for wiring Identity + a dashboard.
- Dribbble tags `patient-portal`, `healthcare-dashboard` — card-layout polish only, never branding.

---

## Principle: calm, trustworthy, health-literate

This is a public hospital, not a private clinic. Tone = reassuring, plain-language, no clinical jargon. Assume some patients use older phones with low digital literacy. Labels say what a layperson understands ("Recent Lab Work" rather than "Labs result panel pending review").

---

## Flow (end-to-end)

1. **Landing (public)** → two clear calls-to-action: **Create an account** and **Sign in**.
2. **Register** (Razor Page, `Areas/Identity/Pages/Account/Register.cshtml`) → creates an `ApplicationUser` with profile fields (Full Name, Contact Number) persisted via EF Core. On success → user is signed in and redirected to the **Home/Dashboard**.
3. **Login** (Razor Page) → `SignInManager.PasswordSignInAsync`. On success → **Home/Dashboard**. Include **Forgot password** (token flow).
4. **Home/Dashboard** (`[Authorize]`, `/Patient/Home`) → summary cards backed by seeded data.

---

## Identity flow conventions (from same-stack references)

- The Identity model is extended (`ApplicationUser` : `IdentityUser`) with real patient profile fields. These are persisted through a migration — never bolted on without persistence.
- Registration collects the extra profile fields on the Register page so the profile is complete at first sign-in.
- After login, redirect to the dashboard; after register (auto-sign-in), redirect to the dashboard.
- The dashboard shows the logged-in user's identity (greeting with Full Name) and links to Identity's manage-account area (`/Identity/Account/Manage`).
- Out-of-scope features are never presented as broken links. If a card maps to an out-of-scope module, it is either non-interactive or links to an already-built real page (e.g., the Departments section on Landing).

---

## Card-driven dashboard (from OpenEMR / Dribbble patient-portal IA)

Keep the dashboard to **4–6 summary cards** (max, per the well-documented "patients use 2 tabs" lesson from portals like MyChart):

1. **Next Appointment** — department, date, status. (Seeded display-only.)
2. **Recent Lab Results** — plain-language status. (Seeded display-only.)
3. **Messages** — unread count. (Seeded display-only.)
4. **My Profile** — real account fields; links to manage-account.
5. **Clinical Departments** — reuses real Landing content (links to Landing's departments section).
6. *(optional)* **Announcements** — static text.

Card anatomy: title (brand-blue semibold), value/status, one action link. Equal height, consistent spacing, mobile-friendly (stack to 1 column).

---

## Accessibility & older-phone handling

- `lang="en"`. Skip-to-content link. Landmarks. Logical heading order (h1 → h2 → h3, no skips).
- Form fields: visible labels, not placeholders-only. Large tap targets. Clear validation messages that appear near the field they describe.
- Touch targets ≥44px on mobile. Focus-visible outlines. Contrast ≥4.5:1.
- Content reflows to one column at 320px (GWTD).

---

## Known-limitation notes (recorded for the README, never shown as UI text)

- **Forgot password** uses Identity's default token flow. Because there is no configured SMTP for this build, the reset link is logged to the development console/stdout rather than emailed. This is documented in the README as a known limitation — never rendered as "demo" text in the UI.
- All dashboard data behind the cards is **seeded** (a small number of rows). The dashboard cards read the seeded rows from the database; they are not hardcoded in markup, and they are display-only in this scope (no booking/labs/messages modules behind them).
