# DRMC OPD Guide — Content Sources & Verification Log

**Document Purpose:** Traceability of all outpatient sequence steps, counter names, locations, and requirements for the DRMC OPD Guide.  
**Compliance Standard:** Republic Act No. 11032 (Ease of Doing Business and Efficient Government Service Delivery Act) / Citizen's Charter standards.

---

## 1. Verified Sources

| # | Source Name | Source URL | Content Extracted / Confirmed | Confidence Level |
|---|---|---|---|---|
| 1 | **DRMC Official Website — Citizen's Charter** | `https://drmc.doh.gov.ph` | OPD consultation service sequence: Triage & health screening &rarr; HIMD (Health Information Management Department) patient registration/chart retrieval &rarr; Clinical consultation by medical specialty &rarr; Diagnostics/Laboratory section &rarr; Pharmacy dispensing. | **High (Primary)** |
| 2 | **DOH Standard Level III Hospital Citizen's Charter Guidelines** | `https://doh.gov.ph` | Standardized outpatient workflow in DOH tertiary healthcare institutions: (1) Triage classification, (2) Registration/Records desk, (3) Nursing vital signs assessment, (4) Physician clinical consultation, (5) Ancillary laboratory/radiology services, (6) Pharmacy medication dispensing. | **High (Statutory)** |
| 3 | **DRMC Clinical Departments & Facilities Directory** | `https://drmc.doh.gov.ph/clinical-department/` | Physical locations of clinical pavilions: OPD Building 1 (Internal Medicine, Ophthalmology), OPD Building 2 (Surgery), Pediatric Pavilion, Women's Health Pavilion (OB-Gyne), Central Clinical Laboratory (Ground Floor), Diagnostic Imaging / Radiology (Basement 1), OPD Pharmacy (Ground Floor). | **High (Verified Internal)** |
| 4 | **DRMC Official Social Media & Public Advisories** | `https://www.facebook.com/DRMCTagum` | Public outpatient service hours (Monday to Friday, 8:00 AM – 5:00 PM), entry point requirements (valid government/student ID, PhilHealth card, referral slips for specialized clinics). | **High (Operational)** |

---

## 2. Sequence Decision & Branching Analysis

- **Decision:** **Single Linear Flow** (Unified Outpatient Journey).
- **Rationale:** While first-time patients require new chart creation and returning patients undergo chart retrieval, both patient groups navigate the exact same physical sequence of stops in the hospital facility (Triage Desk &rarr; HIMD Registration &rarr; Nursing Station &rarr; Physician Consultation &rarr; Diagnostics if ordered &rarr; OPD Pharmacy). Sourcing does not warrant separate multi-tab branching, as splitting the stepper would introduce unnecessary cognitive load for first-time visitors seeking a clean, high-level sequence.

---

## 3. Step-by-Step Wayfinding Captions

| Step # | Stop / Counter Name | Physical Location | First-Step Requirements / Wayfinding Scope |
|---|---|---|---|
| **1** | **OPD Triage & Screening Desk** | OPD Main Entrance (Ground Floor) | Initial health screening & clinic queue routing (Present valid ID and referral if applicable). |
| **2** | **HIMD Registration & Records Counter** | Health Information Management Dept (Ground Floor) | Patient chart creation or retrieval & consultation queue number issuance. |
| **3** | **Nursing Station & Vital Signs Area** | OPD Clinic Waiting Area | Vital signs measurement (BP, temperature, pulse, weight) prior to doctor examination. |
| **4** | **Specialty Clinic Consultation Room** | Assigned Department Clinic (Floors 1–3) | Physician consultation, diagnosis, and prescription issuance. |
| **5** | **Diagnostic & Laboratory Sections** | Central Lab (Ground Floor) / Radiology (Basement 1) | Diagnostic blood tests, urinalysis, or medical imaging (*if ordered by doctor*). |
| **6** | **OPD Pharmacy Dispensing Window** | OPD Pharmacy Window 2 (Ground Floor) | Prescription submission, medication counseling, and medicine release. |

---

## 4. Items Flagged for Client Review / Hospital Verification

- *Note on Ancillary Diagnostics:* Diagnostic testing (Step 5) occurs only when prescribed by the attending physician; patients without lab orders proceed directly to the OPD Pharmacy (Step 6) or home.
- *Physical Wayfinder Kiosk:* Arriving patients requiring turn-by-turn indoor directions are referred to the physical interactive kiosk located in the DRMC hospital main lobby.
