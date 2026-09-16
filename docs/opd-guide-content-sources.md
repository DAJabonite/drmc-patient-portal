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
| 5 | **DRMC OPD Appointment Portal** | `https://medical.drmc.com.ph/opdservices` | Requests a PhilHealth PIN, advises patients to bring their Member Data Record (MDR) if available, and asks for a government-issued ID or a birth-certificate photocopy for infants and children. | **High (Primary operational)** |
| 6 | **WAI-ARIA Authoring Practices — Accordion Pattern** | `https://www.w3.org/WAI/ARIA/apg/patterns/accordion/` | Defines an accordion as vertically stacked interactive headings that reveal or hide their associated content sections, including required button and `aria-expanded` semantics. | **High (Authoritative interaction guidance)** |

---

## 2. Sequence Decision & Verification Convention

- **Decision:** Show the four facility-specific flows in a single-open accordion: Main OPD, BUCAS OPD, Cancer Center for Mindanao OPD (CCM OPD), and Ambulatory Care Center OPD (ACC OPD). Each flow opens immediately below its facility heading, and all flows remain closed until a user activates a facility trigger.
- **Reason:** The supplied workflow material describes different stop counts and routing rules for each facility. Combining them into one generic sequence would obscure the Main OPD appointment/PhilHealth branches, the longer BUCAS post-consultation path, and the service-based ACC queue.
- **Interaction rationale:** WAI-ARIA's accordion pattern is intended for vertically stacked headings that reveal their associated sections. This fits the long, mobile-first facility flows better than a separated vertical tab list and keeps the selected facility and its steps in the same visual context.
- **Verification convention:** The flow text below comes from the stakeholder-supplied rebuild brief. It is implemented as provisional patient guidance until DRMC validates it against a current signed Citizen's Charter or facility workflow. A physical location is displayed only where the supplied material identifies one; no floor, room, window, hour, or telephone detail is inferred.

---

## 3. Main OPD Flow

| Stage | Stop / title | Location shown | Guidance | Verification status |
|---|---|---|---|---|
| Pre-registration | PACD (Public Assistance and Complaints Desk) | Main Entrance | Ask for information and obtain the form. Staff check whether the patient has an appointment. With an appointment, proceed to the specified clinic; without one, obtain a number and begin triage. | **Pending DRMC confirmation**, including the entrance wording and appointment-routing rule. |
| 1 | Triage Nurse | — | Present the queue number and answer the nurse's initial screening questions. | **Pending DRMC confirmation.** Concise guidance is inferred from the supplied stage name. |
| 2 | Vital Signs | — | Staff record basic vital signs before screening. | **Pending DRMC confirmation.** Concise guidance is inferred from the supplied stage name. |
| 3 | Screening / Verification | — | Staff check for an existing record, follow-up status, and PhilHealth. Have the PhilHealth MDR ready if available. Patients with PhilHealth proceed to the PCU for member verification; patients without PhilHealth are directed to the Social Worker for registration. | The **MDR reminder is verified** by the DRMC OPD appointment portal. The PCU and Social Worker routing remain **pending DRMC confirmation**. |
| 4 | Registration | — | Register new patients; pull existing patient records. Bring a government-issued ID, or a birth-certificate photocopy for an infant or child. | The **document reminder is verified** by the DRMC OPD appointment portal; record-handling wording remains **pending DRMC confirmation**. |
| 5 | Cashier | — | Newly registered patients with no existing record pay ₱150. Senior citizens, PWDs, and other eligible patients pay ₱0 upon presentation of a qualifying ID. | **Stakeholder-supplied guidance; pending DRMC confirmation.** |
| 6 | Clinic | — | Proceed to the intended clinic. | **Pending DRMC confirmation.** |

---

## 4. BUCAS OPD Flow

| Step | Stop / title | Location shown | Guidance | Verification status |
|---|---|---|---|---|
| 1 | Queue Number | — | On arrival, obtain a queueing number from the guard. | **Pending DRMC confirmation.** |
| 2 | Vital Signs | VS (Vital Signs) Area | Wait in the VS area for initial assessment. | **Pending DRMC confirmation**, including the official name of the VS area. |
| 3 | PhilHealth Verification | — | Proceed to PhilHealth verification and have the MDR ready if available. | The **MDR reminder is verified** for DRMC OPD generally; its application at BUCAS remains **pending DRMC confirmation**. |
| 4 | Registration | — | Proceed to patient registration after verification. | **Pending DRMC confirmation.** |
| 5 | Nurse Triage | — | The triage nurse evaluates the condition and determines the appropriate consultation process. | **Pending DRMC confirmation.** |
| 6 | Wait for Consultation | — | Monitor the queueing screen for the queue number to be called. | **Pending DRMC confirmation.** |
| 7 | Doctor's Consultation | — | Once called, proceed to the designated OPD consultation cubicle. | **Pending DRMC confirmation.** |
| 8 | Post-Consultation Instructions | — | Follow the doctor's orders to Pharmacy, Laboratory, X-ray Department, or another required diagnostic or treatment service. | **Pending DRMC confirmation**, including the official service names. |
| 9 | Follow-Up (conditional) | — | If follow-up is required, complete requested laboratory tests, X-rays, or other examinations before returning according to staff instructions. | **Pending DRMC confirmation.** |

---

## 5. Cancer Center for Mindanao OPD (CCM OPD) Flow

| Step | Stop / title | Location shown | Guidance | Verification status |
|---|---|---|---|---|
| 1 | Queue | — | Follow staff instructions to obtain or wait for a queue number. | **Provisional; pending DRMC confirmation.** |
| 2 | Registration | — | Provide the information requested so staff can create or retrieve the patient record. | **Provisional; pending DRMC confirmation.** |
| 3 | Vital Signs | — | Staff record basic vital signs before triage. | **Provisional; pending DRMC confirmation.** |
| 4 | Triage | — | Briefly explain the concern so staff can direct the patient to the appropriate clinic. | **Provisional; pending DRMC confirmation.** |
| 5 | Clinic | — | Wait for the name or queue number, then proceed when called. | **Provisional; pending DRMC confirmation.** |

The page displays a non-alarming “For verification” note for this panel. No counter name, room number, floor, operating hour, or detailed instruction is shown because none is supported by the supplied material.

---

## 6. Ambulatory Care Center OPD (ACC OPD) Flow

| Step | Stop / title | Location shown | Guidance | Verification status |
|---|---|---|---|---|
| 1 | Queue by Service | — | Queue by Day Surgery, Digestive Endoscopy, Ultrasound & 2D Echo, or Consultation. | **Pending DRMC confirmation**, including the official service labels. |
| 2 | Registration | — | Provide the information requested so staff can create or retrieve the patient record. | **Pending DRMC confirmation.** |
| 3 | Vital Signs | — | Staff record basic vital signs before the nurse-station check. | **Pending DRMC confirmation.** |
| 4 | Nurse Station | — | Staff verify the service type. | **Pending DRMC confirmation.** |
| 5 | Clinic | — | Wait for the name or queue number, then proceed when called. | **Pending DRMC confirmation.** |

---

## 7. Items Requiring DRMC Confirmation

1. Confirm all four sequence orders, stop names, and whether the steps apply to new, returning, referred, and appointment patients in the same way.
2. Confirm that the Main OPD PACD is correctly described as being at the “Main Entrance.”
3. Provide the official expansion and patient-facing name for “PCU,” and confirm the PhilHealth / no-PhilHealth branch in Main OPD Step 3.
4. Confirm whether the BUCAS “VS (Vital Signs) Area” is the official physical wayfinding label.
5. Confirm the CCM OPD five-step sequence; it currently has no supporting location or instruction details.
6. Confirm ACC service names, including punctuation and whether “Ultrasound & 2D Echo” is one queue category.
7. Confirm whether the restart reminder applies to all four facilities or only to a specific OPD triage workflow.
