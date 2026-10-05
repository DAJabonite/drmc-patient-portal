# Import Template Contract v1

Download the actual templates from Admin Imports. Headers and worksheet names are case-sensitive, fixed and ordered. Do not add ID, ownership, timestamp, counter, rowversion, audit or Identity columns. Empty optional fields are allowed; required fields and enum values follow the Admin forms. Strings have the forms' SQL lengths and the 100,000-character cap. Slugs and clinical references use the forms' normalization and explicit case-insensitive duplicate checks.

## Common Context

Patient and clinical templates begin with `SourcePatientKey,SourcePatientName,SourceBirthDate`. Every patient/clinical row needs a non-empty source patient key, at most 200 characters. The name and birth-date columns are optional review hints, not matching rules. Staff reconcile every distinct, case-sensitive group manually. Patient-template fields determine the new record; the extra source hints do not override FullName or DateOfBirth.

Encounters, labs and prescriptions additionally require `SourceRecordKey`, a non-empty label unique within that parent template in the batch. Lab rows also have optional `SourceEncounterKey`; lab items require `SourceLabKey`; dose schedules require `SourcePrescriptionKey`. Parent-reference values use `batch:<SourceRecordKey>` for a row in this workbook or `existing:<hospital reference>` for an existing encounter reference, lab accession number or Rx number. Prefixes are case-sensitive; references are compared explicitly without case sensitivity. Parent references are bounded to 450 characters including the prefix. Missing or ambiguous parents and cross-patient relationships block the batch. Parent rows may appear after their children in a workbook; validation orders them by dependency. Children always inherit ownership from the verified parent.

CSV uploads target one template only; children can reference existing parents. Workbook sheets can combine templates and batch parent keys. Multiple patient labels may deliberately map to one existing patient, but new groups are never merged automatically. Patient-template rows must explicitly create new records, not map to existing ones. Duplicate hospital numbers block creation; missing hospital numbers stay null and retain no generated value. Same-name patients are not automatically considered the same identity.

## Worksheet Names and Entity Columns

The common context columns above precede the following columns where applicable. Downloaded headers are the authoritative ordered contract.

| Worksheet | Additional context before entity columns | Entity columns |
| --- | --- | --- |
| Patients_v1 | Common context | FullName, DateOfBirth, HospitalNumber |
| Doctors_v1 | None | FullName, Title, Department, SubSpecialty, ClinicRoom, ScheduleSummary, OffersTeleconsult, Biography, PrcLicenseMasked, IsActive |
| PublicAdvisories_v1 | None | Title, Slug, Category, Priority, Summary, ContentHtml, ImageUrl, IssuingUnit, PublishedAt, EffectiveUntil, IsPinned |
| ClinicalEncounters_v1 | Common context, SourceRecordKey | EncounterReference, EncounterDate, Department, Type, HistoricalDoctorName, ChiefComplaint, PrimaryDiagnosis, SecondaryDiagnosis, ClinicalSummary, CarePlanAndInstructions, VitalSignsRecorded, FollowUpDate, FollowUpNotes |
| LabResults_v1 | Common context, SourceRecordKey, SourceEncounterKey | AccessionNumber, TestName, Category, CollectedAt, ReleasedAt, Status, HistoricalDoctorName, Pathologist.HistoricalDoctorName, ResultSummary, PerformingUnit, ClinicalNotes |
| LabResultItems_v1 | Common context, SourceLabKey | ParameterName, Value, Unit, ReferenceRange, Flag |
| Prescriptions_v1 | Common context, SourceRecordKey | RxNumber, GenericName, BrandName, Dosage, DosageForm, Frequency, Instructions, Department, PrescribedAt, ValidUntil, LastRefillDate, RefillsTotal, RefillsRemaining, Status, HistoricalDoctorName |
| MedicationDoseSchedules_v1 | Common context, SourcePrescriptionKey | DoseTime, DisplayOrder |
| PatientAllergies_v1 | Common context | Allergen, Reaction, Severity, RecordedAt |

## Value Formats

- Dates: `yyyy-MM-dd`; date/time: `yyyy-MM-ddTHH:mm:ss` with up to seven fractional digits. No time-zone suffix is accepted. Clinical values are Manila wall time, and advisory publication/effectivity input represents UTC. Operational timestamps are generated in UTC, never imported.
- Dose time: `HH:mm`, `HH:mm:ss`, or fractional seconds accepted by the dose form. Excel time/date cells are converted to these invariant formats. Source identifiers and hospital numbers must be text cells, never numeric cells.
- Booleans: `true` or `false`; an empty Boolean cell is false. Integers are invariant base-10 whole numbers. Enums use an exact declared enum name or a defined numeric value; out-of-range values fail validation. Department must be an exact canonical name from ClinicalDepartment.cs. Lab status must be Available, In progress or Pending Verification.
- CSV: UTF-8, optional UTF-8 BOM, comma delimiter, double-quoted fields with doubled quotes. Embedded commas and newlines work inside quoted fields. Formula-like CSV text is treated as data and is never evaluated or exported into spreadsheets. Workbooks containing any formula are rejected, even if the formula has a cached result.
- Physician imports require explicit HistoricalDoctorName snapshots (and the pathologist snapshot for labs). Directory IDs are not accepted as import columns. Staff may use the ordinary forms for a directory picker.
- Advisory content is sanitized using the same allowlist as the form. Links must be HTTPS or safe local relative URLs. Scripts, styles, forms, embeds and event attributes do not survive sanitization.

Existing duplicate reference numbers, slugs, hospital numbers or dose times block the batch. Import-only duplicate checks also block same-directory-name/department Doctors, same-parent lab parameters and same-patient allergy names. These are record-duplicate warnings, not identity matching or merge rules.

## Operational Limits

At most 20 MB and 10,000 data rows per batch; reconciliation and preview pages display 25 rows/groups. The encrypted envelope is additionally capped at 100 MB including mappings and validation results. Approval locks file hash, mappings and validation version 1.0.0. Maintainers must increment the validation version when changing import semantics. No partial batch is retained if any record or audit entry fails.

Two independently started processes may share the queue during recycle or redeploy. They must use the same build, database, application identity, key-protection setting, key directory and staging directory. Execution is single-flight; dry run and approval may overlap. Running phases with expired leases become Failed without automatic replay; live leases remain untouched and queued phases retain their batch ID. Review final status, cancel failed uploads and resubmit only after reconciliation. Key protection is opt-in; see the README Key Protection and Recovery section before enabling DPAPI. Source volume, representative exports and hospital retention/access policies still need client input.

Dry runs, approval and execution all run on the durable worker. Requests queue the phase and return to the polling status page. Duplicate and relationship checks load existing keys in bounded set-based queries and check them in memory; execution repeats validation inside the serializable business/audit transaction. The row limit is a safety bound, not a latency guarantee. Choose batch sizes using representative source files and deployment capacity. No partial business or per-record audit writes survive a failed execution.
