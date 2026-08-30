# DRMC Patient Portal — Security Review Todo & Boundary Register

**Document Version:** 1.0.0  
**Phase:** 3.2 (Authenticated Dashboard Features)  
**Last Updated:** August 26, 2026  
**Audience:** Security Engineers, Backend Architects, DOH / NPC Data Protection Officers (DPO)

---

## Overview

During Phase 3.2, real Protected Health Information (PHI)-adjacent functionality was introduced to the authenticated portal area. The user flows, database models, controllers, and patient-facing views are completely operational and backed by EF Core SQLite.

However, deploying with **real Philippine patient data** in a Level III DOH tertiary healthcare institution requires institutional hardening that cannot be completed solely within the client portal application code. This document catalogues every security-review boundary item, its in-code pointers, current functional state, hardening requirements, and review state.

> [!IMPORTANT]
> Every item below contains a corresponding in-code comment at its implementation boundary in `src/DrmcPatientPortal/`. Do not mark items as resolved without genuine institutional and cryptographic review.

---

## Security Boundary Register

### Item 1: Asynchronous Care Communication & Message Storage
- **Area:** Clinical Messaging & Care Coordination
- **In-Code Pointers:**
  - [`src/DrmcPatientPortal/Models/MessageThread.cs`](file:///c:/Users/User/Documents/1Random%20Works/Drmc%20V2/drmc-patient-portal/src/DrmcPatientPortal/Models/MessageThread.cs#L3-L12)
  - [`src/DrmcPatientPortal/Models/Message.cs`](file:///c:/Users/User/Documents/1Random%20Works/Drmc%20V2/drmc-patient-portal/src/DrmcPatientPortal/Models/Message.cs#L3-L9)
  - [`src/DrmcPatientPortal/Controllers/MessagesController.cs`](file:///c:/Users/User/Documents/1Random%20Works/Drmc%20V2/drmc-patient-portal/src/DrmcPatientPortal/Controllers/MessagesController.cs#L10-L19)
- **What's Real Today:**
  - Full threaded conversation model between patients and clinical departments (Internal Medicine, OPD Pharmacy, etc.).
  - Category routing (Lab Result Clarification, Medication Question, Care Guidance, General Inquiry).
  - Chronological message streams with sender roles (`Patient`, `CareTeam`, `Doctor`), read receipt timestamps, and dynamic unread badge count synchronization.
- **Hardening Requirements for Security Engineer / Backend Review:**
  1. **Field/Column-Level Encryption-at-Rest (AES-256-GCM):** Encrypt `Message.Body` and sensitive subject headers before database write using keys managed by a Hardware Security Module (HSM) or Azure Key Vault / AWS KMS.
  2. **Clinical Role-Based Access Control (RBAC):** Implement department-level authorization policies ensuring only licensed healthcare staff assigned to that specific clinic can decrypt and read patient message streams.
  3. **Automated Rate Limiting:** Apply token-bucket rate limiting per patient to prevent clinical staff inbox flooding or denial of service.
  4. **Antivirus / Attachment Screening:** When file attachments (e.g. photos of rashes, external prescription scans) are enabled in future phases, integrate real-time ICAP / ClamAV malware scanning.
- **Why It Can't Ship As-Is for Real Patient Data:**
  - Unencrypted message bodies in database storage violate DOH Health Information Privacy policies and National Privacy Commission (NPC) Data Privacy Act of 2012 (RA 10173) standards for digital transmission of health consultation notes.
- **Review State:** `TODO`

---

### Item 2: Caregiver & Proxy Access Trust Model
- **Area:** Proxy Access, Dependent Management & Identity Verification
- **In-Code Pointers:**
  - [`src/DrmcPatientPortal/Models/DependentProfile.cs`](file:///c:/Users/User/Documents/1Random%20Works/Drmc%20V2/drmc-patient-portal/src/DrmcPatientPortal/Models/DependentProfile.cs#L3-L12)
  - [`src/DrmcPatientPortal/Controllers/ProxyController.cs`](file:///c:/Users/User/Documents/1Random%20Works/Drmc%20V2/drmc-patient-portal/src/DrmcPatientPortal/Controllers/ProxyController.cs#L10-L18)
- **What's Real Today:**
  - Dependent profile registration capturing Full Name, DOB, Gender, Relationship (`Child`, `Parent`, `Spouse`, `Sibling`, `LegalWard`), PhilHealth Number, and ID document reference (PSA Birth Certificate, OSCA Senior ID).
  - Explicit statutory caregiver declaration and consent checkbox agreement.
  - In-session profile context switching via ASP.NET Core session state (`ActiveDependentId`, `ActiveDependentName`), enabling guardians to view and manage records on behalf of dependents.
  - Automated audit logging of every proxy switch event.
- **Hardening Requirements for Security Engineer / Backend Review:**
  1. **Documentary Identity Verification:** Implement an institutional document verification workflow (e.g., HIMD / Medical Records staff verification of uploaded PSA birth certificate or legal guardianship decree) before a proxy relationship transitions from `PendingVerification` to `Active`.
  2. **Age-of-Majority Automatic Expiration:** Automated scheduled job to expire caregiver proxy access when a minor dependent reaches 18 years of age (age of majority under Philippine Law), requiring direct patient re-authorization.
  3. **Multi-Party Revocation:** Mechanism for either party (or hospital legal counsel) to immediately revoke caregiver proxy delegation.
- **Why It Can't Ship As-Is for Real Patient Data:**
  - Self-attested proxy relationships without institutional HIMD document review could allow unauthorized individuals to access confidential medical records of third parties.
- **Review State:** `TODO`

---

### Item 3: PHI Access Audit Logging & Tamper Resistance
- **Area:** Compliance, Access Transparency & Tamper-Evident Auditing
- **In-Code Pointers:**
  - [`src/DrmcPatientPortal/Models/AuditLog.cs`](file:///c:/Users/User/Documents/1Random%20Works/Drmc%20V2/drmc-patient-portal/src/DrmcPatientPortal/Models/AuditLog.cs#L3-L12)
  - [`src/DrmcPatientPortal/Services/AuditLogService.cs`](file:///c:/Users/User/Documents/1Random%20Works/Drmc%20V2/drmc-patient-portal/src/DrmcPatientPortal/Services/AuditLogService.cs#L6-L16)
- **What's Real Today:**
  - Structured audit logging tracking every security-sensitive operation: `VIEW_LAB_REPORT`, `PRINT_LAB_REPORT`, `VIEW_ENCOUNTER_SUMMARY`, `PRINT_ENCOUNTER_SUMMARY`, `VIEW_PRESCRIPTION`, `REQUEST_REFILL`, `SUBMIT_TRIAGE_INTAKE`, `EMERGENCY_RED_FLAG_TRIGGERED`, `CREATE_MESSAGE_THREAD`, `REPLY_MESSAGE_THREAD`, `ADD_DEPENDENT_PROFILE`, `PROXY_SWITCH`, `PROXY_SWITCH_SELF`.
  - Records `UserId`, `Action`, `Resource`, `Details`, `IpAddress`, and UTC `Timestamp` to both EF Core SQLite `AuditLogs` table and ASP.NET Core `ILogger`.
- **Hardening Requirements for Security Engineer / Backend Review:**
  1. **Append-Only / WORM or Remote SIEM Streaming:** Forward audit events in real-time to an immutable remote log collector (e.g. Syslog over TLS, Apache Kafka, Azure Sentinel, AWS CloudWatch) to prevent local log tampering.
  2. **DOH 10-Year Log Retention Policy:** Configure immutable storage tiers and lifecycle retention rules matching DOH health records compliance (10-year statutory retention).
  3. **PII Masking in Details Payload:** Ensure no unmasked biometric data, full PhilHealth numbers, or raw passwords are ever serialized into audit log message bodies.
- **Why It Can't Ship As-Is for Real Patient Data:**
  - Local database audit tables can be modified or purged if an attacker compromises the application database server.
- **Review State:** `TODO`

---

### Item 4: Pre-Consultation Self-Triage Clinician Integration
- **Area:** Clinical Workstation Integration & EMR Interoperability
- **In-Code Pointers:**
  - [`src/DrmcPatientPortal/Models/TriageIntake.cs`](file:///c:/Users/User/Documents/1Random%20Works/Drmc%20V2/drmc-patient-portal/src/DrmcPatientPortal/Models/TriageIntake.cs#L3-L9)
  - [`src/DrmcPatientPortal/Controllers/TriageController.cs`](file:///c:/Users/User/Documents/1Random%20Works/Drmc%20V2/drmc-patient-portal/src/DrmcPatientPortal/Controllers/TriageController.cs#L10-L16)
- **What's Real Today:**
  - Patient pre-consultation symptom intake form pre-loaded with appointment context.
  - Visual 0-10 pain scale, symptom duration, self-reported vitals (BP, temperature, heart rate, weight, blood sugar), and comorbidity checkboxes.
  - Acuity classification (`Routine`, `Priority`, `UrgentEmergency`) and emergency red-flag safeguard intercept.
  - Digital summary pass with appointment linking.
- **Hardening Requirements for Security Engineer / Backend Review:**
  1. **Clinician Workstation View:** Build a dedicated, clinician-authenticated EMR view for attending physicians to review and acknowledge self-triage answers prior to calling the patient into the clinic room.
  2. **HL7 FHIR / DOH EMR Synchronization:** Map triage intake data to FHIR `QuestionnaireResponse` and `Observation` resources for interoperability with DRMC's central Hospital Information System (HIS).
- **Why It Can't Ship As-Is for Real Patient Data:**
  - The patient portal captures the intake cleanly; clinical workstation ingestion requires separate clinical authentication and HIS bus integration.
- **Review State:** `TODO`

---

### Item 5: Government ID Photo Storage, Retention & Cryptographic Hardening
- **Area:** Government ID Verification & Document Storage
- **In-Code Pointers:**
  - [`src/DrmcPatientPortal/Models/PatientIdDocument.cs`](file:///c:/Users/User/Documents/1Random%20Works/Drmc%20V2/drmc-patient-portal/src/DrmcPatientPortal/Models/PatientIdDocument.cs#L3-L20)
  - [`src/DrmcPatientPortal/Controllers/PatientDocumentsController.cs`](file:///c:/Users/User/Documents/1Random%20Works/Drmc%20V2/drmc-patient-portal/src/DrmcPatientPortal/Controllers/PatientDocumentsController.cs#L10-L25)
  - [`src/DrmcPatientPortal/Services/TesseractIdDocumentExtractionService.cs`](file:///c:/Users/User/Documents/1Random%20Works/Drmc%20V2/drmc-patient-portal/src/DrmcPatientPortal/Services/TesseractIdDocumentExtractionService.cs#L8-L30)
- **What's Real Today:**
  - Fully local, offline OCR extraction pipeline for 8 Philippine government ID types using Tesseract with label-anchored and MRZ parsers.
  - Captured front and back ID photos are stored outside `wwwroot` in `App_Data/PatientIdDocuments/{patientId}/`.
  - Photos are strictly not reachable via public static URLs; accessed exclusively through the authorized endpoint `PatientDocumentsController.IdPhoto` which enforces patient identity ownership and records an access audit event.
  - Structured extraction metadata and confidence scores are persisted in `PatientIdDocuments`.
- **Hardening Requirements for Security Engineer / Backend Review:**
  1. **Storage-Tier Encryption-at-Rest (AES-256-GCM / Envelope Encryption):** Encrypt binary photo payloads before saving to disk storage using envelope keys managed by HSM / Azure Key Vault / AWS KMS, rather than storing unencrypted image files on the local filesystem.
  2. **NPC / RA 10173 Data Retention & Purge Policy:** Formulate and enforce a Data Protection Officer (DPO)-approved retention schedule where sensitive raw ID images are automatically purged or permanently redacted once hospital records staff complete identity verification, retaining only the audit metadata.
  3. **Antivirus & Malware Screening Pipeline:** Implement streaming ICAP or ClamAV daemon antivirus inspection on all uploaded multipart photo streams prior to OCR processing or disk persistence.
  4. **Legal & DPO Consent Language Certification:** Submit the privacy consent copy to DRMC legal counsel and the National Privacy Commission for formal certification regarding biometric data handling and photo retention under Philippine law.
- **Why It Can't Ship As-Is for Real Patient Data:**
  - Plaintext disk storage of government identity documents lacks cryptographic envelope encryption and automated NPC-mandated data destruction lifecycles.
- **Review State:** `TODO`

---

## Status Summary

| Item # | Title | Scope Area | Current Implementation | Security Review State |
|---|---|---|---|---|
| **1** | Asynchronous Care Communication | Messaging & Storage | Fully operational threaded messaging | `TODO` |
| **2** | Caregiver & Proxy Access Trust Model | Identity & Delegation | Operational profile switching & consent | `TODO` |
| **3** | PHI Access Audit Logging | Security & Compliance | Operational EF Core + ILogger audit trail | `TODO` |
| **4** | Pre-Consultation Self-Triage Sync | Clinical Workstation | Operational patient intake & acuity engine | `TODO` |
| **5** | Government ID Photo Storage & Retention | ID Verification & Storage | Secure non-wwwroot storage & authorized access | `TODO` |

*This document must remain active and maintained throughout Phase 3.3 and subsequent production security reviews.*
