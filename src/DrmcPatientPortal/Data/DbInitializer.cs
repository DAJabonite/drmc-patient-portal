using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Data;

// Seeds the database with realistic patient accounts, clinical staff roster,
// live queue boards, assistance guidelines, and advisories. Runs only in Development.
// Seed credentials are documented in README.md; nothing "seed"-like is ever
// rendered in patient-facing UI text.
public static class DbInitializer
{
    public static void Initialize(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        // Ensure the schema exists so a fresh `dotnet run` works out of the box
        db.Database.Migrate();

        // 1. Seed Doctors Roster (16 clinical specialists across the 8 verified departments)
        if (!db.Doctors.Any())
        {
            var doctors = new List<Doctor>
            {
                new()
                {
                    FullName = "Dr. Arthur Llanos",
                    Title = "MD, FPCP, FPSMO",
                    Department = "Internal Medicine",
                    SubSpecialty = "Adult Medical Oncology & General Internal Medicine",
                    ClinicRoom = "OPD Bldg 1, Room 201",
                    ScheduleSummary = "Mon / Wed / Fri · 8:00 AM - 12:00 PM",
                    OffersTeleconsult = true,
                    Biography = "Senior Consultant in Internal Medicine with subspecialty training in Medical Oncology. Chairperson of DRMC Cancer Care Committee.",
                    PrcLicenseMasked = "PRC Lic. No. 0089***"
                },
                new()
                {
                    FullName = "Dr. Maria Elena Cruz",
                    Title = "MD, FPCP, FPCCP",
                    Department = "Internal Medicine",
                    SubSpecialty = "Pulmonary Medicine & Critical Care",
                    ClinicRoom = "OPD Bldg 1, Room 202",
                    ScheduleSummary = "Tue / Thu / Sat · 9:00 AM - 1:00 PM",
                    OffersTeleconsult = true,
                    Biography = "Specialist in adult asthma, COPD, pulmonary tuberculosis, and post-infectious lung conditions.",
                    PrcLicenseMasked = "PRC Lic. No. 0094***"
                },
                new()
                {
                    FullName = "Dr. Roberto Valderama",
                    Title = "MD, FPCS, FPSGS",
                    Department = "Surgery",
                    SubSpecialty = "General, Gastrointestinal & Laparoscopic Surgery",
                    ClinicRoom = "OPD Bldg 2, Room 101",
                    ScheduleSummary = "Mon / Tue / Thu · 8:00 AM - 12:00 PM",
                    OffersTeleconsult = false,
                    Biography = "Chief of Surgical Services with expertise in minimally invasive laparoscopic cholecystectomy, hernia repairs, and gastrointestinal oncology.",
                    PrcLicenseMasked = "PRC Lic. No. 0078***"
                },
                new()
                {
                    FullName = "Dr. Dennis Alcantara",
                    Title = "MD, FPOA",
                    Department = "Surgery",
                    SubSpecialty = "Orthopedic Surgery & Musculoskeletal Trauma",
                    ClinicRoom = "OPD Bldg 2, Room 103",
                    ScheduleSummary = "Wed / Fri · 1:00 PM - 5:00 PM",
                    OffersTeleconsult = true,
                    Biography = "Orthopedic consultant managing acute fractures, joint replacements, and sports injuries.",
                    PrcLicenseMasked = "PRC Lic. No. 0091***"
                },
                new()
                {
                    FullName = "Dr. Carmela Bautista",
                    Title = "MD, FPPS",
                    Department = "Pediatrics",
                    SubSpecialty = "General Pediatrics & Preventive Child Health",
                    ClinicRoom = "Pediatric Pavilion, Room 101",
                    ScheduleSummary = "Mon to Fri · 8:00 AM - 12:00 PM",
                    OffersTeleconsult = true,
                    Biography = "General pediatrician focusing on developmental milestones, childhood nutrition, immunization, and common pediatric infections.",
                    PrcLicenseMasked = "PRC Lic. No. 0096***"
                },
                new()
                {
                    FullName = "Dr. Paolo Gabriel Ruiz",
                    Title = "MD, FPPS, DPSNbM",
                    Department = "Pediatrics",
                    SubSpecialty = "Neonatology & Newborn Critical Care",
                    ClinicRoom = "Pediatric Pavilion, Room 104",
                    ScheduleSummary = "Tue / Thu / Sat · 1:00 PM - 4:00 PM",
                    OffersTeleconsult = true,
                    Biography = "Neonatologist managing high-risk newborn follow-ups, premature infant development, and neonatal nutrition.",
                    PrcLicenseMasked = "PRC Lic. No. 0102***"
                },
                new()
                {
                    FullName = "Dr. Stephanie Joy Garcia",
                    Title = "MD, FPOGS",
                    Department = "OB-Gyne",
                    SubSpecialty = "Maternal & Fetal Medicine / High-Risk Pregnancy",
                    ClinicRoom = "Women's Health Center, Room 201",
                    ScheduleSummary = "Mon / Wed / Fri · 8:30 AM - 12:30 PM",
                    OffersTeleconsult = true,
                    Biography = "Perinatologist specializing in complex maternal health conditions, gestational diabetes, and fetal surveillance.",
                    PrcLicenseMasked = "PRC Lic. No. 0087***"
                },
                new()
                {
                    FullName = "Dr. Teresa Morales",
                    Title = "MD, FPOGS, FSGOP",
                    Department = "OB-Gyne",
                    SubSpecialty = "Gynecologic Oncology & Reproductive Health",
                    ClinicRoom = "Women's Health Center, Room 203",
                    ScheduleSummary = "Tue / Thu · 9:00 AM - 2:00 PM",
                    OffersTeleconsult = false,
                    Biography = "Consultant in screening and surgical management of gynecologic malignancies, abnormal uterine bleeding, and cervical health.",
                    PrcLicenseMasked = "PRC Lic. No. 0082***"
                },
                new()
                {
                    FullName = "Dr. Cristina Ramos",
                    Title = "MD, FPAFP",
                    Department = "Family & Community Medicine",
                    SubSpecialty = "Preventive Medicine, Chronic Disease & Holistic Care",
                    ClinicRoom = "Primary Care Pavilion, Room 101",
                    ScheduleSummary = "Mon to Fri · 8:00 AM - 4:00 PM",
                    OffersTeleconsult = true,
                    Biography = "Family physician leading the DRMC PhilHealth Konsulta program, providing continuing healthcare for entire families across life stages.",
                    PrcLicenseMasked = "PRC Lic. No. 0095***"
                },
                new()
                {
                    FullName = "Dr. Manuel Ocampo",
                    Title = "MD, FPAFP",
                    Department = "Family & Community Medicine",
                    SubSpecialty = "Geriatric Medicine & Palliative Home Care",
                    ClinicRoom = "Primary Care Pavilion, Room 103",
                    ScheduleSummary = "Mon / Wed / Fri · 1:00 PM - 5:00 PM",
                    OffersTeleconsult = true,
                    Biography = "Specialist in senior citizen health optimization, polypharmacy management, and long-term care coordination.",
                    PrcLicenseMasked = "PRC Lic. No. 0090***"
                },
                new()
                {
                    FullName = "Dr. Victorino Lim",
                    Title = "MD, FPSA",
                    Department = "Anesthesiology",
                    SubSpecialty = "Pain Management & Pre-Anesthesia Assessment",
                    ClinicRoom = "Surgical Complex, Room 301",
                    ScheduleSummary = "Mon to Fri · 8:00 AM - 12:00 PM",
                    OffersTeleconsult = true,
                    Biography = "Consultant in perioperative anesthesia safety, interventional acute and chronic pain management.",
                    PrcLicenseMasked = "PRC Lic. No. 0084***"
                },
                new()
                {
                    FullName = "Dr. Jocelyn Tan",
                    Title = "MD, FPSA",
                    Department = "Anesthesiology",
                    SubSpecialty = "Obstetric & Pediatric Anesthesia",
                    ClinicRoom = "Surgical Complex, Room 302",
                    ScheduleSummary = "Tue / Thu · 8:00 AM - 2:00 PM",
                    OffersTeleconsult = false,
                    Biography = "Specialist in safe regional anesthesia for labor and delivery, and delicate pediatric surgical procedures.",
                    PrcLicenseMasked = "PRC Lic. No. 0086***"
                },
                new()
                {
                    FullName = "Dr. Ramon Guingona",
                    Title = "MD, FPAO",
                    Department = "Ophthalmology",
                    SubSpecialty = "Cataract & Comprehensive Ophthalmology",
                    ClinicRoom = "Eye Care Center, Room 301",
                    ScheduleSummary = "Mon / Wed / Thu · 8:00 AM - 12:00 PM",
                    OffersTeleconsult = true,
                    Biography = "Ophthalmic surgeon specializing in modern micro-incision phacoemulsification for cataracts and routine visual acuity correction.",
                    PrcLicenseMasked = "PRC Lic. No. 0093***"
                },
                new()
                {
                    FullName = "Dr. Patricia Sison",
                    Title = "MD, FPAO",
                    Department = "Ophthalmology",
                    SubSpecialty = "Medical Retina & Glaucoma",
                    ClinicRoom = "Eye Care Center, Room 303",
                    ScheduleSummary = "Tue / Fri · 9:00 AM - 1:00 PM",
                    OffersTeleconsult = true,
                    Biography = "Retina specialist focused on diabetic retinopathy screening, retinal laser procedures, and long-term glaucoma control.",
                    PrcLicenseMasked = "PRC Lic. No. 0097***"
                },
                new()
                {
                    FullName = "Dr. Francis Xavier Gomez",
                    Title = "MD, FPCR",
                    Department = "Radiology",
                    SubSpecialty = "Computed Tomography (CT) & MRI Imaging",
                    ClinicRoom = "Diagnostic Imaging Center, B1",
                    ScheduleSummary = "Mon to Fri · 8:00 AM - 4:00 PM",
                    OffersTeleconsult = false,
                    Biography = "Diagnostic radiologist leading DRMC multi-slice CT and magnetic resonance imaging interpretations.",
                    PrcLicenseMasked = "PRC Lic. No. 0081***"
                },
                new()
                {
                    FullName = "Dr. Katrina Dizon",
                    Title = "MD, FPCR, FUSP",
                    Department = "Radiology",
                    SubSpecialty = "Ultrasound, Doppler & Women's Imaging",
                    ClinicRoom = "Diagnostic Imaging Center, B1",
                    ScheduleSummary = "Mon / Wed / Fri · 8:00 AM - 12:00 PM",
                    OffersTeleconsult = false,
                    Biography = "Specialist in diagnostic general ultrasound, vascular Doppler examinations, and digital mammography.",
                    PrcLicenseMasked = "PRC Lic. No. 0098***"
                }
            };

            db.Doctors.AddRange(doctors);
            db.SaveChanges();
        }

        // 2. Seed Assistance Programs (Statutory Philippine Government Health Assistance)
        if (!db.AssistancePrograms.Any())
        {
            var programs = new List<AssistanceProgram>
            {
                new()
                {
                    Code = "MALASAKIT",
                    Title = "Malasakit Center One-Stop Shop (RA 11463)",
                    ManagingAgency = "DOH / DSWD / PhilHealth / PCSO",
                    Description = "A unified one-stop shop in DRMC designed to streamline access to financial and medical assistance for indigent and financially-incapacitated Filipino patients.",
                    CoverageScope = "Hospitalization Bills, Diagnostic Tests, Surgical Procedures, Prescribed Medications",
                    EligibilitySummary = "All Filipino citizens, prioritizing indigent, low-income, senior citizens, and PWD patients receiving care at Davao Regional Medical Center.",
                    RequiredDocumentsJson = """["Valid Government ID (PhilSys / PhilHealth / Voter's / Driver's)", "Original Medical Certificate or Clinical Abstract from DRMC Physician", "Official Hospital Statement of Account or Laboratory / Drug Price Quotation", "Barangay Certificate of Indigency / Certificate of Eligibility from LGU", "Social Case Study Report (for high-value financial assistance requests)"]""",
                    StepByStepProcedureJson = """["Step 1: Obtain a Medical Certificate / Treatment Order and Cost Quotation from your DRMC Attending Physician or Billing Desk.", "Step 2: Proceed to the DRMC Medical Social Service Unit (MSSU) for initial patient intake classification.", "Step 3: Submit your unified document envelope at the Malasakit Center Intake Desk (Ground Floor, Main Bldg).", "Step 4: The unified Malasakit intake officer assesses coverage across PhilHealth, DOH MAIP, and PCSO.", "Step 5: Receive your approved Guarantee Letter / Discount Voucher to present to the DRMC Billing and Pharmacy counters."]""",
                    OfficeLocation = "Malasakit Center, Ground Floor, DRMC Main Hospital Building (Near OPD Atrium)",
                    OperatingHours = "Monday to Friday, 7:00 AM - 5:00 PM (Emergency Window open 24/7)"
                },
                new()
                {
                    Code = "MAIP",
                    Title = "DOH Medical Assistance for Indigent Patients (MAIP)",
                    ManagingAgency = "Department of Health (DOH)",
                    Description = "Direct medical assistance grant funding hospitalization, implants, specialized medicines, and diagnostic procedures for in-need patients.",
                    CoverageScope = "Emergency Care, Hemodialysis, Chemotherapy Drugs, Laboratory Workups, Surgical Implants",
                    EligibilitySummary = "Patients classified under Classes C3 and D by the DRMC Medical Social Service Unit.",
                    RequiredDocumentsJson = """["Barangay Certificate of Indigency (specifying purpose: Medical Assistance)", "Clinical Abstract / Doctor's Prescription / Laboratory Request with Doctor's PRC License", "Valid ID of Patient and Authorized Representative", "DRMC Price Quotation / Official Hospital Billing Statement"]""",
                    StepByStepProcedureJson = """["Step 1: Secure physician prescription / order with DRMC hospital stamp.", "Step 2: Get quotation from DRMC Pharmacy or Diagnostic Laboratory.", "Step 3: Present documents to DOH MAIP officer inside the Malasakit Center.", "Step 4: Officer issues approved MAIP charge slip covering the requested medical service."]""",
                    OfficeLocation = "Malasakit Center Desk 2, Ground Floor",
                    OperatingHours = "Monday to Friday, 8:00 AM - 4:00 PM"
                },
                new()
                {
                    Code = "PHILHEALTH",
                    Title = "PhilHealth Konsulta & Universal Healthcare Benefits",
                    ManagingAgency = "Philippine Health Insurance Corporation",
                    Description = "Primary care package covering consultation, targeted diagnostic laboratory tests, and prescribed maintenance drugs under the Universal Health Care Act.",
                    CoverageScope = "Free Consultation, CBC, Urinalysis, Fasting Blood Sugar, Lipid Profile, Chest X-Ray, Maintenance Drugs",
                    EligibilitySummary = "All registered PhilHealth members and legal dependents with active DRMC Konsulta facility registration.",
                    RequiredDocumentsJson = """["PhilHealth Identification Card (PIC) or Member Data Record (MDR)", "PhilHealth Konsulta Registration Slip (available at DRMC Registration Desk)", "Valid Photo Government ID"]""",
                    StepByStepProcedureJson = """["Step 1: Register DRMC as your accredited PhilHealth Konsulta provider at the Primary Care Pavilion.", "Step 2: Undergo initial health profiling with a Family Medicine physician.", "Step 3: Avail covered laboratory and diagnostic exams with zero co-payment.", "Step 4: Receive prescribed essential maintenance medications at the DRMC Konsulta Pharmacy."]""",
                    OfficeLocation = "Primary Care Pavilion, Ground Floor & Malasakit Desk 1",
                    OperatingHours = "Monday to Friday, 8:00 AM - 5:00 PM"
                },
                new()
                {
                    Code = "DSWD_AICS",
                    Title = "DSWD Assistance to Individuals in Crisis Situation (AICS)",
                    ManagingAgency = "Department of Social Welfare and Development",
                    Description = "Direct social safety-net financial grant for critical medical treatments, prosthetics, wheelchairs, and post-hospitalization rehabilitation.",
                    CoverageScope = "Prosthetics, Orthopedic Hardware, Assistive Devices, Medical Transportation",
                    EligibilitySummary = "Families facing acute socio-economic crisis resulting from catastrophic medical emergencies.",
                    RequiredDocumentsJson = """["Medical Abstract signed by DRMC Physician with date of issuance", "Original Doctor's Prescription / Device Quotation", "Barangay Certificate of Indigency and Proof of Residency", "Valid ID of Representative and Authorization Letter"]""",
                    StepByStepProcedureJson = """["Step 1: Undergo interview with DRMC DSWD Social Worker Desk.", "Step 2: Submit required clinical summary and cost estimate.", "Step 3: Receive DSWD Guarantee Letter for hospital billing adjustment."]""",
                    OfficeLocation = "Malasakit Center Desk 3, Ground Floor",
                    OperatingHours = "Monday to Friday, 8:00 AM - 3:00 PM"
                },
                new()
                {
                    Code = "PCSO",
                    Title = "PCSO Individual Medical Assistance Program (IMAP)",
                    ManagingAgency = "Philippine Charity Sweepstakes Office",
                    Description = "Charity assistance grant targeting high-cost specialty medical care such as chemotherapy, dialysis, hemodialysis supplies, and advanced diagnostic imaging.",
                    CoverageScope = "Chemotherapy, Radiation Therapy, Dialysis, CT Scan / MRI, Specialty Surgery",
                    EligibilitySummary = "Patients undergoing prolonged or high-cost therapies with remaining balance after PhilHealth.",
                    RequiredDocumentsJson = """["Official Statement of Account or Prescription / Treatment Protocol", "Clinical Abstract signed with Doctor's PRC number", "Valid Government ID of Patient & Immediate Relative", "Barangay Certificate of Indigency"]""",
                    StepByStepProcedureJson = """["Step 1: Obtain treatment protocol / quotation from attending oncologist or nephrologist.", "Step 2: Submit files to PCSO Desk at Malasakit Center.", "Step 3: Approved PCSO Guarantee Letter applies directly against procedure fees."]""",
                    OfficeLocation = "Malasakit Center Desk 4, Ground Floor",
                    OperatingHours = "Monday to Friday, 8:00 AM - 3:00 PM"
                }
            };

            db.AssistancePrograms.AddRange(programs);
            db.SaveChanges();
        }

        // 3. Seed Public Health Advisories (DRMC & DOH Region XI)
        if (!db.PublicAdvisories.Any())
        {
            var advisories = new List<PublicAdvisory>
            {
                new()
                {
                    Title = "Dengue 4S Prevention & Surveillance Alert — Davao del Norte",
                    Slug = "dengue-4s-prevention-alert",
                    Category = AdvisoryCategory.HealthAlert,
                    Priority = AdvisoryPriority.Urgent,
                    Summary = "Davao Regional Medical Center strengthens 24/7 fever triage and reminds the public to practice the 4S strategy against mosquito-borne infections.",
                    ContentHtml = """
                    <p>In alignment with the Department of Health Regional Office XI surveillance advisory, Davao Regional Medical Center (DRMC) has placed its Emergency Outpatient Fever Triage on heightened status.</p>
                    <h5>Remember the 4S Strategy:</h5>
                    <ul>
                        <li><strong>Search and Destroy:</strong> Eliminate mosquito breeding sites in and around household premises.</li>
                        <li><strong>Self-Protection Measures:</strong> Wear long-sleeved clothing and utilize mosquito repellent.</li>
                        <li><strong>Seek Early Consultation:</strong> Consult immediately at the nearest health center or DRMC OPD if fever lasts for more than 2 days.</li>
                        <li><strong>Support Fogging/Spraying:</strong> Only in hotspot areas where increases in cases are registered.</li>
                    </ul>
                    <p>DRMC Outpatient Department provides daily blood examinations (CBC with Platelet Count) at the Central Laboratory.</p>
                    """,
                    IssuingUnit = "DRMC Infection Control & Public Health Unit",
                    PublishedAt = DateTime.UtcNow.AddDays(-2),
                    IsPinned = true,
                    ViewCount = 1420
                },
                new()
                {
                    Title = "Expanded National Immunization Schedule at DRMC Pediatric Pavilion",
                    Slug = "expanded-immunization-schedule-pediatrics",
                    Category = AdvisoryCategory.Vaccination,
                    Priority = AdvisoryPriority.Normal,
                    Summary = "Free routine immunization vaccines (BCG, Hepatitis B, Pentavalent, Oral Polio, Measles-Rubella) available every Wednesday and Friday.",
                    ContentHtml = """
                    <p>The DRMC Department of Pediatrics announces the updated schedule for free routine childhood vaccinations under the DOH National Immunization Program (NIP).</p>
                    <p><strong>Clinic Days:</strong> Every Wednesday and Friday, 8:00 AM - 12:00 PM<br/>
                    <strong>Location:</strong> Pediatric Pavilion, Ground Floor<br/>
                    <strong>Requirements:</strong> Child's Baby Book / Immunization Card and Parent/Guardian Valid ID.</p>
                    """,
                    IssuingUnit = "Department of Pediatrics — DRMC",
                    PublishedAt = DateTime.UtcNow.AddDays(-5),
                    IsPinned = false,
                    ViewCount = 890
                },
                new()
                {
                    Title = "Animal Bite Treatment Center (ABTC) 24/7 Triage & Vaccination Advisory",
                    Slug = "animal-bite-treatment-center-guidelines",
                    Category = AdvisoryCategory.HospitalNotice,
                    Priority = AdvisoryPriority.High,
                    Summary = "Post-exposure prophylaxis (PEP) protocols for animal scratches and bites. Free anti-rabies vaccination is provided per DOH guidelines.",
                    ContentHtml = """
                    <p>Davao Regional Medical Center operates an accredited Animal Bite Treatment Center (ABTC) providing immediate wound assessment, rabies post-exposure prophylaxis, and tetanus immunization.</p>
                    <h5>Immediate First Aid Measures:</h5>
                    <ol>
                        <li>Wash the bite/scratch wound vigorously with clean running water and soap for at least 15 minutes.</li>
                        <li>Apply antiseptic solution (Povidone Iodine or 70% alcohol).</li>
                        <li>Do not apply garlic, tandok, or traditional concoctions to the wound.</li>
                        <li>Proceed immediately to the DRMC ABTC Unit for category assessment and vaccine administration.</li>
                    </ol>
                    """,
                    IssuingUnit = "DRMC Animal Bite Treatment Center",
                    PublishedAt = DateTime.UtcNow.AddDays(-8),
                    IsPinned = false,
                    ViewCount = 1150
                },
                new()
                {
                    Title = "Free PhilHealth Konsulta Registration Drive at OPD Main Atrium",
                    Slug = "philhealth-konsulta-registration-drive",
                    Category = AdvisoryCategory.Advisory,
                    Priority = AdvisoryPriority.Normal,
                    Summary = "Register DRMC as your official Konsulta primary care facility to access free checkups, laboratory diagnostic tests, and maintenance medicines.",
                    ContentHtml = """
                    <p>All PhilHealth members and their qualified dependents are invited to register at the DRMC PhilHealth Konsulta desk. Avail free comprehensive laboratory packages (CBC, Fasting Blood Sugar, Lipid Profile, Chest X-Ray) upon consultation with our Family Medicine physicians.</p>
                    """,
                    IssuingUnit = "DRMC Universal Health Care Implementation Unit",
                    PublishedAt = DateTime.UtcNow.AddDays(-12),
                    IsPinned = false,
                    ViewCount = 670
                },
                new()
                {
                    Title = "Outpatient Specialty Clinic Schedules & Holiday Operations Advisory",
                    Slug = "outpatient-specialty-clinic-holiday-schedules",
                    Category = AdvisoryCategory.HospitalNotice,
                    Priority = AdvisoryPriority.Normal,
                    Summary = "Advisory on OPD clinic booking schedules and Emergency Department continuous 24/7 operations.",
                    ContentHtml = """
                    <p>Please be advised that while Outpatient Specialty Clinics observe declared national public holidays, the <strong>DRMC Emergency Department, Trauma Center, Delivery Room, and Inpatient Wards remain fully operational 24 hours a day, 7 days a week</strong>.</p>
                    <p>Online appointments scheduled on non-working holidays may be rescheduled through the portal without penalty.</p>
                    """,
                    IssuingUnit = "Office of the Medical Center Chief",
                    PublishedAt = DateTime.UtcNow.AddDays(-15),
                    IsPinned = false,
                    ViewCount = 520
                },
                new()
                {
                    Title = "Preventing Water-Borne & Gastrointestinal Illnesses during Rainy Season",
                    Slug = "waterborne-illness-prevention-rainy-season",
                    Category = AdvisoryCategory.SeasonalHealth,
                    Priority = AdvisoryPriority.Normal,
                    Summary = "Guidance from DRMC Public Health Unit on clean drinking water protocols, food hygiene, and leptospirosis prophylaxis.",
                    ContentHtml = """
                    <p>Heavy rainfall can compromise local water supplies. The DRMC Public Health Unit advises all communities to boil drinking water for at least 2 minutes if the source is untreated, practice hand hygiene, and avoid wading in floodwaters to prevent leptospirosis.</p>
                    """,
                    IssuingUnit = "DRMC Public Health Unit",
                    PublishedAt = DateTime.UtcNow.AddDays(-20),
                    IsPinned = false,
                    ViewCount = 410
                }
            };

            db.PublicAdvisories.AddRange(advisories);
            db.SaveChanges();
        }

        // 4. Seed Live OPD Queue Tickets across multiple clinical departments
        if (!db.QueueTickets.Any())
        {
            var now = DateTime.UtcNow;
            var tickets = new List<QueueTicket>
            {
                new() { TicketNumber = "IM-102", Department = "Internal Medicine", ClinicRoom = "Room 201 - Dr. Llanos", Status = QueueTicketStatus.Serving, IsPriority = false, IssuedAt = now.AddMinutes(-45), CalledAt = now.AddMinutes(-8), ServedAt = now.AddMinutes(-6), EstimatedWaitMinutes = 0 },
                new() { TicketNumber = "IM-103", Department = "Internal Medicine", ClinicRoom = "Room 202 - Dr. Cruz", Status = QueueTicketStatus.Called, IsPriority = false, IssuedAt = now.AddMinutes(-35), CalledAt = now.AddMinutes(-2), EstimatedWaitMinutes = 5 },
                new() { TicketNumber = "IM-104", Department = "Internal Medicine", ClinicRoom = "Room 201 - Dr. Llanos", Status = QueueTicketStatus.Waiting, IsPriority = true, IssuedAt = now.AddMinutes(-20), EstimatedWaitMinutes = 15 },
                new() { TicketNumber = "PED-018", Department = "Pediatrics", ClinicRoom = "Room 101 - Dr. Bautista", Status = QueueTicketStatus.Serving, IsPriority = false, IssuedAt = now.AddMinutes(-50), CalledAt = now.AddMinutes(-12), ServedAt = now.AddMinutes(-10), EstimatedWaitMinutes = 0 },
                new() { TicketNumber = "PED-019", Department = "Pediatrics", ClinicRoom = "Room 104 - Dr. Ruiz", Status = QueueTicketStatus.Called, IsPriority = false, IssuedAt = now.AddMinutes(-30), CalledAt = now.AddMinutes(-1), EstimatedWaitMinutes = 5 },
                new() { TicketNumber = "PED-020", Department = "Pediatrics", ClinicRoom = "Room 101 - Dr. Bautista", Status = QueueTicketStatus.Waiting, IsPriority = false, IssuedAt = now.AddMinutes(-15), EstimatedWaitMinutes = 20 },
                new() { TicketNumber = "SUR-007", Department = "Surgery", ClinicRoom = "Room 101 - Dr. Valderama", Status = QueueTicketStatus.Serving, IsPriority = false, IssuedAt = now.AddMinutes(-60), CalledAt = now.AddMinutes(-15), ServedAt = now.AddMinutes(-12), EstimatedWaitMinutes = 0 },
                new() { TicketNumber = "SUR-008", Department = "Surgery", ClinicRoom = "Room 103 - Dr. Alcantara", Status = QueueTicketStatus.Waiting, IsPriority = false, IssuedAt = now.AddMinutes(-25), EstimatedWaitMinutes = 15 },
                new() { TicketNumber = "OBG-022", Department = "OB-Gyne", ClinicRoom = "Room 201 - Dr. Garcia", Status = QueueTicketStatus.Serving, IsPriority = true, IssuedAt = now.AddMinutes(-40), CalledAt = now.AddMinutes(-5), ServedAt = now.AddMinutes(-3), EstimatedWaitMinutes = 0 },
                new() { TicketNumber = "OBG-023", Department = "OB-Gyne", ClinicRoom = "Room 203 - Dr. Morales", Status = QueueTicketStatus.Waiting, IsPriority = false, IssuedAt = now.AddMinutes(-20), EstimatedWaitMinutes = 20 },
                new() { TicketNumber = "FCM-031", Department = "Family & Community Medicine", ClinicRoom = "Room 101 - Dr. Ramos", Status = QueueTicketStatus.Serving, IsPriority = false, IssuedAt = now.AddMinutes(-30), CalledAt = now.AddMinutes(-7), ServedAt = now.AddMinutes(-5), EstimatedWaitMinutes = 0 },
                new() { TicketNumber = "RAD-042", Department = "Radiology", ClinicRoom = "X-Ray Room 1", Status = QueueTicketStatus.Called, IsPriority = false, IssuedAt = now.AddMinutes(-25), CalledAt = now.AddMinutes(-3), EstimatedWaitMinutes = 5 }
            };

            db.QueueTickets.AddRange(tickets);
            db.SaveChanges();
        }

        // 5. Seed Users & Patient Accounts
        var seededUsers = new List<(string email, string password, string firstName, string? middleName, string lastName, string contact, string idType, string idNumber)>
        {
            ("patient@drmc.doh.gov.ph", "P@tient2026", "Maria Clara", "D.", "Santos", "0917 123 4567", "Philippine National ID (PhilSys)", "1234-5678-9012-3456"),
            ("juan@drmc.doh.gov.ph", "J@uan2026",     "Juan Miguel",  "A.", "Dela Cruz", "0918 765 4321", "PhilHealth ID", "12-345678901-2"),
        };

        foreach (var (email, password, firstName, middleName, lastName, contact, idType, idNumber) in seededUsers)
        {
            if (db.Users.Any(u => u.Email == email))
            {
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = firstName,
                MiddleName = middleName,
                LastName = lastName,
                FullName = string.IsNullOrWhiteSpace(middleName) ? $"{firstName} {lastName}" : $"{firstName} {middleName} {lastName}",
                ContactNumber = contact,
                PhoneNumber = contact,
                IdType = idType,
                IdNumber = idNumber,
                PrivacyConsent = true,
                CreatedAt = DateTime.UtcNow,
            };

            userManager.CreateAsync(user, password).GetAwaiter().GetResult();
        }

        // 6. Seed Reviewer Patient Data (Appointments, Labs, Messages, Queue Ticket Link)
        var primary = db.Users.FirstOrDefault(u => u.Email == "patient@drmc.doh.gov.ph");
        if (primary is not null)
        {
            var doctorLlanos = db.Doctors.FirstOrDefault(d => d.FullName.Contains("Llanos"));
            var doctorRamos = db.Doctors.FirstOrDefault(d => d.FullName.Contains("Ramos"));

            if (!db.Appointments.Any(a => a.PatientUserId == primary.Id))
            {
                db.Appointments.AddRange(
                    new Appointment
                    {
                        BookingReference = "DRMC-2026-IM-0192",
                        PatientUserId = primary.Id,
                        PatientName = primary.FullName,
                        ContactNumber = primary.ContactNumber,
                        Email = primary.Email ?? "patient@drmc.doh.gov.ph",
                        PhilHealthNumber = "12-345678901-2",
                        Department = "Internal Medicine",
                        DoctorId = doctorLlanos?.Id,
                        DoctorName = doctorLlanos?.FullName ?? "Dr. Arthur Llanos",
                        Type = "In-Person OPD",
                        ScheduledAt = DateTime.Now.AddDays(12).Date.AddHours(9).AddMinutes(30),
                        TimeSlot = "09:30 AM - 10:00 AM",
                        ChiefComplaint = "Routine 3-month follow-up for blood pressure and fasting blood sugar management.",
                        Status = "Confirmed",
                        QrCodePayload = "DRMC|REF:DRMC-2026-IM-0192|PAT:Maria Clara Santos|DEPT:Internal Medicine|DATE:20260907-0930|SIG:VERIFIED_DOH_DRMC",
                        CreatedAt = DateTime.UtcNow.AddDays(-2)
                    },
                    new Appointment
                    {
                        BookingReference = "DRMC-2026-TC-0481",
                        PatientUserId = primary.Id,
                        PatientName = primary.FullName,
                        ContactNumber = primary.ContactNumber,
                        Email = primary.Email ?? "patient@drmc.doh.gov.ph",
                        PhilHealthNumber = "12-345678901-2",
                        Department = "Family & Community Medicine",
                        DoctorId = doctorRamos?.Id,
                        DoctorName = doctorRamos?.FullName ?? "Dr. Cristina Ramos",
                        Type = "Teleconsultation",
                        ScheduledAt = DateTime.Now.AddMonths(1).Date.AddHours(14),
                        TimeSlot = "02:00 PM - 02:30 PM",
                        ChiefComplaint = "PhilHealth Konsulta preventive wellness consultation.",
                        Status = "Pending",
                        QrCodePayload = "DRMC|REF:DRMC-2026-TC-0481|PAT:Maria Clara Santos|DEPT:Family & Community Medicine|DATE:20260926-1400|SIG:VERIFIED_DOH_DRMC",
                        TeleconsultMeetingUrl = "https://telehealth.drmc.doh.gov.ph/consult/room-tc-0481",
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    });
            }

            // Link a queue ticket to Maria Clara Santos so she can test the ticket lookup
            if (!db.QueueTickets.Any(q => q.PatientUserId == primary.Id))
            {
                db.QueueTickets.Add(new QueueTicket
                {
                    TicketNumber = "IM-105",
                    Department = "Internal Medicine",
                    ClinicRoom = "Room 201 - Dr. Llanos",
                    Status = QueueTicketStatus.Waiting,
                    IsPriority = false,
                    IssuedAt = DateTime.UtcNow.AddMinutes(-10),
                    EstimatedWaitMinutes = 25,
                    PatientUserId = primary.Id
                });
            }

            if (!db.LabResults.Any(l => l.PatientUserId == primary.Id))
            {
                var cbc = new LabResult
                {
                    PatientUserId = primary.Id,
                    AccessionNumber = "DRMC-LAB-2026-0814",
                    TestName = "Complete Blood Count (CBC) with Platelet Count",
                    Category = LabCategory.Hematology,
                    CollectedAt = DateTime.UtcNow.AddDays(-6),
                    ReleasedAt = DateTime.UtcNow.AddDays(-5),
                    Status = "Available",
                    ResultSummary = "Hematology parameters within standard diagnostic limits. No acute cytopenia.",
                    OrderingPhysician = "Dr. Arthur Llanos, MD, FPCP",
                    PathologistName = "Dr. Manuel Santos, MD, FPSP",
                    PerformingUnit = "DRMC Central Clinical Diagnostic Laboratory",
                    ClinicalNotes = "Routine pre-consultation baseline workup."
                };

                cbc.Items.Add(new LabResultItem { ParameterName = "Hemoglobin", Value = "13.8", Unit = "g/dL", ReferenceRange = "12.0 - 16.0", Flag = LabFlag.Normal });
                cbc.Items.Add(new LabResultItem { ParameterName = "Hematocrit", Value = "41.2", Unit = "%", ReferenceRange = "37.0 - 48.0", Flag = LabFlag.Normal });
                cbc.Items.Add(new LabResultItem { ParameterName = "White Blood Cells (WBC)", Value = "7.5", Unit = "x10^9/L", ReferenceRange = "4.5 - 11.0", Flag = LabFlag.Normal });
                cbc.Items.Add(new LabResultItem { ParameterName = "Platelet Count", Value = "280", Unit = "x10^9/L", ReferenceRange = "150 - 450", Flag = LabFlag.Normal });
                cbc.Items.Add(new LabResultItem { ParameterName = "Segmenters (Neutrophils)", Value = "60", Unit = "%", ReferenceRange = "50 - 70", Flag = LabFlag.Normal });
                cbc.Items.Add(new LabResultItem { ParameterName = "Lymphocytes", Value = "32", Unit = "%", ReferenceRange = "20 - 40", Flag = LabFlag.Normal });
                cbc.Items.Add(new LabResultItem { ParameterName = "Monocytes", Value = "4", Unit = "%", ReferenceRange = "2 - 8", Flag = LabFlag.Normal });
                cbc.Items.Add(new LabResultItem { ParameterName = "Eosinophils", Value = "4", Unit = "%", ReferenceRange = "1 - 4", Flag = LabFlag.Normal });

                var fbs = new LabResult
                {
                    PatientUserId = primary.Id,
                    AccessionNumber = "DRMC-LAB-2026-0815",
                    TestName = "Fasting Blood Sugar (FBS)",
                    Category = LabCategory.ClinicalChemistry,
                    CollectedAt = DateTime.UtcNow.AddDays(-6),
                    ReleasedAt = DateTime.UtcNow.AddDays(-5),
                    Status = "Available",
                    ResultSummary = "Elevated fasting blood glucose. Consistent with impaired fasting glycemia / DM monitoring.",
                    OrderingPhysician = "Dr. Arthur Llanos, MD, FPCP",
                    PathologistName = "Dr. Manuel Santos, MD, FPSP",
                    PerformingUnit = "DRMC Clinical Chemistry Section",
                    ClinicalNotes = "Patient confirmed 10-hour overnight fasting."
                };

                fbs.Items.Add(new LabResultItem { ParameterName = "Fasting Blood Glucose", Value = "112", Unit = "mg/dL", ReferenceRange = "70 - 99", Flag = LabFlag.High });

                var lipid = new LabResult
                {
                    PatientUserId = primary.Id,
                    AccessionNumber = "DRMC-LAB-2026-0790",
                    TestName = "Lipid Profile Panel",
                    Category = LabCategory.ClinicalChemistry,
                    CollectedAt = DateTime.UtcNow.AddDays(-20),
                    ReleasedAt = DateTime.UtcNow.AddDays(-19),
                    Status = "Available",
                    ResultSummary = "Lipid parameters within acceptable cardiovascular risk limits.",
                    OrderingPhysician = "Dr. Arthur Llanos, MD, FPCP",
                    PathologistName = "Dr. Manuel Santos, MD, FPSP",
                    PerformingUnit = "DRMC Clinical Chemistry Section"
                };

                lipid.Items.Add(new LabResultItem { ParameterName = "Total Cholesterol", Value = "195", Unit = "mg/dL", ReferenceRange = "< 200", Flag = LabFlag.Normal });
                lipid.Items.Add(new LabResultItem { ParameterName = "Triglycerides", Value = "140", Unit = "mg/dL", ReferenceRange = "< 150", Flag = LabFlag.Normal });
                lipid.Items.Add(new LabResultItem { ParameterName = "HDL Cholesterol", Value = "48", Unit = "mg/dL", ReferenceRange = "> 40", Flag = LabFlag.Normal });
                lipid.Items.Add(new LabResultItem { ParameterName = "LDL Cholesterol", Value = "119", Unit = "mg/dL", ReferenceRange = "< 130", Flag = LabFlag.Normal });

                var hba1c = new LabResult
                {
                    PatientUserId = primary.Id,
                    AccessionNumber = "DRMC-LAB-2026-0922",
                    TestName = "HbA1c (Glycated Hemoglobin)",
                    Category = LabCategory.SpecialDiagnostics,
                    CollectedAt = DateTime.UtcNow.AddDays(-2),
                    Status = "In progress",
                    ResultSummary = "Specimen received by laboratory. Analysis currently in progress.",
                    OrderingPhysician = "Dr. Arthur Llanos, MD, FPCP",
                    PerformingUnit = "DRMC Central Clinical Diagnostic Laboratory"
                };

                db.LabResults.AddRange(cbc, fbs, lipid, hba1c);
            }

            if (!db.ClinicalEncounters.Any(e => e.PatientUserId == primary.Id))
            {
                db.ClinicalEncounters.AddRange(
                    new ClinicalEncounter
                    {
                        PatientUserId = primary.Id,
                        EncounterReference = "DRMC-ENC-2026-0412",
                        EncounterDate = DateTime.UtcNow.AddDays(-18),
                        Department = "Internal Medicine",
                        AttendingPhysician = "Dr. Arthur Llanos, MD, FPCP",
                        Type = EncounterType.OpdConsultation,
                        ChiefComplaint = "3-month routine follow-up for chronic blood sugar and blood pressure management.",
                        PrimaryDiagnosis = "Essential (Primary) Hypertension (ICD-10 I10)",
                        SecondaryDiagnosis = "Type 2 Diabetes Mellitus without complications (ICD-10 E11.9)",
                        ClinicalSummary = "Patient is asymptomatic. No chest pain, shortness of breath, or blurring of vision. Home blood pressure logs average 125/80 mmHg. Good medication adherence reported.",
                        CarePlanAndInstructions = "1. Maintain low-sodium, low-glycemic diet.\n2. Regular aerobic exercise (30 mins daily brisk walking).\n3. Continue Losartan 50mg OD morning and Metformin 500mg BID with meals.\n4. Repeat Fasting Blood Sugar and HbA1c in 3 months.",
                        VitalSignsRecorded = "BP: 128/82 mmHg | HR: 76 bpm | Temp: 36.5 C | Wt: 64.0 kg | Height: 158 cm | BMI: 25.6",
                        FollowUpDate = DateTime.UtcNow.AddMonths(3),
                        FollowUpNotes = "Follow-up consultation at Internal Medicine OPD Room 201."
                    },
                    new ClinicalEncounter
                    {
                        PatientUserId = primary.Id,
                        EncounterReference = "DRMC-ENC-2025-1089",
                        EncounterDate = DateTime.UtcNow.AddMonths(-6),
                        Department = "Family & Community Medicine",
                        AttendingPhysician = "Dr. Cristina Ramos, MD, FPAFP",
                        Type = EncounterType.OpdConsultation,
                        ChiefComplaint = "Annual wellness physical checkup.",
                        PrimaryDiagnosis = "General Adult Medical Examination (ICD-10 Z00.0)",
                        ClinicalSummary = "Complete physical examination unremarkable. Cardiac auscultation normal, lungs clear. Screening mammography and cervical smear updated.",
                        CarePlanAndInstructions = "Promote continued healthy lifestyle and routine seasonal influenza vaccination.",
                        VitalSignsRecorded = "BP: 120/78 mmHg | HR: 72 bpm | Temp: 36.6 C | Wt: 63.5 kg"
                    }
                );
            }

            if (!db.Prescriptions.Any(p => p.PatientUserId == primary.Id))
            {
                var rx1 = new Prescription
                {
                    PatientUserId = primary.Id,
                    RxNumber = "DRMC-RX-2026-3819",
                    GenericName = "Metformin Hydrochloride",
                    BrandName = "Glucophage",
                    Dosage = "500 mg",
                    DosageForm = "Film-coated Tablet",
                    Frequency = "Twice daily with meals (8:00 AM, 6:00 PM)",
                    Instructions = "Take with or immediately after breakfast and dinner to minimize gastrointestinal discomfort.",
                    PrescribingDoctor = "Dr. Arthur Llanos, MD, FPCP",
                    Department = "Internal Medicine",
                    PrescribedAt = DateTime.UtcNow.AddDays(-18),
                    ValidUntil = DateTime.UtcNow.AddMonths(6),
                    Status = PrescriptionStatus.Active,
                    RefillsTotal = 3,
                    RefillsRemaining = 2,
                    LastRefillDate = DateTime.UtcNow.AddDays(-18)
                };

                var rx2 = new Prescription
                {
                    PatientUserId = primary.Id,
                    RxNumber = "DRMC-RX-2026-3820",
                    GenericName = "Losartan Potassium",
                    BrandName = "Cozaar",
                    Dosage = "50 mg",
                    DosageForm = "Film-coated Tablet",
                    Frequency = "Once daily every morning (8:00 AM)",
                    Instructions = "Take consistently every morning with or without food. Do not discontinue without physician advice.",
                    PrescribingDoctor = "Dr. Arthur Llanos, MD, FPCP",
                    Department = "Internal Medicine",
                    PrescribedAt = DateTime.UtcNow.AddDays(-18),
                    ValidUntil = DateTime.UtcNow.AddMonths(6),
                    Status = PrescriptionStatus.Active,
                    RefillsTotal = 3,
                    RefillsRemaining = 1,
                    LastRefillDate = DateTime.UtcNow.AddDays(-2)
                };

                rx2.RefillRequests.Add(new RefillRequest
                {
                    PatientUserId = primary.Id,
                    RequestedAt = DateTime.UtcNow.AddDays(-2),
                    Status = RefillStatus.ReadyForPickup,
                    PharmacyNotes = "Approved and packaged. Ready for claiming at DRMC OPD Pharmacy Window 2.",
                    EstimatedPickupDate = DateTime.UtcNow.AddDays(1)
                });

                var rx3 = new Prescription
                {
                    PatientUserId = primary.Id,
                    RxNumber = "DRMC-RX-2026-2104",
                    GenericName = "Ascorbic Acid + Zinc",
                    BrandName = "Cecon Plus",
                    Dosage = "500 mg / 10 mg",
                    DosageForm = "Capsule",
                    Frequency = "Once daily after breakfast",
                    Instructions = "Daily nutritional immune support supplement.",
                    PrescribingDoctor = "Dr. Cristina Ramos, MD, FPAFP",
                    Department = "Family & Community Medicine",
                    PrescribedAt = DateTime.UtcNow.AddMonths(-1),
                    ValidUntil = DateTime.UtcNow.AddMonths(5),
                    Status = PrescriptionStatus.Active,
                    RefillsTotal = 3,
                    RefillsRemaining = 3
                };

                db.Prescriptions.AddRange(rx1, rx2, rx3);
            }

            if (!db.PatientAllergies.Any(a => a.PatientUserId == primary.Id))
            {
                db.PatientAllergies.Add(new PatientAllergy
                {
                    PatientUserId = primary.Id,
                    Allergen = "Penicillin & Beta-lactam Antibiotics",
                    Reaction = "Urticarial skin rash, facial itching, and mild lip swelling.",
                    Severity = AllergySeverity.Moderate,
                    RecordedAt = DateTime.UtcNow.AddYears(-2)
                });
            }

            if (!db.TriageIntakes.Any(t => t.PatientUserId == primary.Id))
            {
                var appt = db.Appointments.FirstOrDefault(a => a.PatientUserId == primary.Id);
                if (appt is not null)
                {
                    db.TriageIntakes.Add(new TriageIntake
                    {
                        AppointmentId = appt.Id,
                        PatientUserId = primary.Id,
                        SubmittedAt = DateTime.UtcNow.AddDays(-1),
                        ChiefComplaint = "Routine 3-month follow-up for blood pressure and diabetes monitoring.",
                        SymptomDurationDays = 5,
                        PainScale = 0,
                        SymptomsJson = "[\"Mild Fatigue\", \"Occasional dry mouth\"]",
                        HasEmergencyRedFlags = false,
                        ReportedBloodPressure = "125/80 mmHg",
                        ReportedTemperature = "36.6 C",
                        ReportedHeartRate = "72 bpm",
                        ReportedWeightKg = "64.0",
                        ReportedBloodSugar = "110 mg/dL",
                        ComorbiditiesJson = "[\"Hypertension\", \"Type 2 Diabetes Mellitus\"]",
                        CurrentMedicationsSummary = "Metformin 500mg BID, Losartan 50mg OD",
                        AcuityLevel = TriageAcuity.Routine,
                        TriageNotes = "Patient pre-screened digitally. Stable vital signs self-reported. No acute distress."
                    });
                }
            }

            if (!db.MessageThreads.Any(t => t.PatientUserId == primary.Id))
            {
                var thread1 = new MessageThread
                {
                    PatientUserId = primary.Id,
                    Department = "Internal Medicine",
                    Subject = "Clarification on Fasting Blood Sugar Lab Schedule",
                    Category = MessageCategory.LabResultClarification,
                    Status = ThreadStatus.Resolved,
                    CreatedAt = DateTime.UtcNow.AddDays(-8),
                    LastMessageAt = DateTime.UtcNow.AddDays(-7)
                };

                thread1.Messages.Add(new Message
                {
                    SenderUserId = primary.Id,
                    SenderName = primary.FullName,
                    SenderRole = MessageSenderRole.Patient,
                    Body = "Good day, do I need to fast for 10 hours or 12 hours for my upcoming Fasting Blood Sugar laboratory test?",
                    SentAt = DateTime.UtcNow.AddDays(-8),
                    IsRead = true,
                    ReadAt = DateTime.UtcNow.AddDays(-8).AddHours(2)
                });

                thread1.Messages.Add(new Message
                {
                    SenderUserId = "staff-internal-med",
                    SenderName = "Nurse Christine Cruz, RN (OPD Coordinator)",
                    SenderRole = MessageSenderRole.CareTeam,
                    Body = "Hello Ma'am Maria. For Fasting Blood Sugar, a 10-to-12 hour overnight fast is standard. You may drink plain water. Please proceed directly to Room 102 at 7:30 AM with your lab request slip.",
                    SentAt = DateTime.UtcNow.AddDays(-8).AddHours(3),
                    IsRead = true,
                    ReadAt = DateTime.UtcNow.AddDays(-7)
                });

                thread1.Messages.Add(new Message
                {
                    SenderUserId = primary.Id,
                    SenderName = primary.FullName,
                    SenderRole = MessageSenderRole.Patient,
                    Body = "Thank you for the clear instructions Nurse Christine!",
                    SentAt = DateTime.UtcNow.AddDays(-7),
                    IsRead = true
                });

                var thread2 = new MessageThread
                {
                    PatientUserId = primary.Id,
                    Department = "OPD Pharmacy",
                    Subject = "Prescription Refill Voucher Ready",
                    Category = MessageCategory.MedicationQuestion,
                    Status = ThreadStatus.Open,
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    LastMessageAt = DateTime.UtcNow.AddHours(-3)
                };

                thread2.Messages.Add(new Message
                {
                    SenderUserId = "staff-pharmacy",
                    SenderName = "Pharmacist Mark Reyes, RPh",
                    SenderRole = MessageSenderRole.CareTeam,
                    Body = "Good day Ma'am Maria. Your refill request for Losartan 50mg (Rx #DRMC-RX-2026-3820) has been approved by Dr. Llanos and is ready for claiming at OPD Pharmacy Window 2. Please bring your Senior/Patient ID.",
                    SentAt = DateTime.UtcNow.AddHours(-3),
                    IsRead = false // Leaves 1 unread message for dashboard counter
                });

                db.MessageThreads.AddRange(thread1, thread2);
            }

            if (!db.DependentProfiles.Any(d => d.GuardianUserId == primary.Id))
            {
                db.DependentProfiles.AddRange(
                    new DependentProfile
                    {
                        GuardianUserId = primary.Id,
                        FullName = "Joshua D. Santos",
                        DateOfBirth = DateTime.UtcNow.AddYears(-8),
                        Gender = "Male",
                        Relationship = RelationshipType.Child,
                        PhilHealthNumber = "19-203948571-3",
                        IdType = "PSA Birth Certificate",
                        IdNumber = "PSA-2018-091823",
                        StatutoryConsentAgreed = true,
                        CreatedAt = DateTime.UtcNow.AddMonths(-4)
                    },
                    new DependentProfile
                    {
                        GuardianUserId = primary.Id,
                        FullName = "Corazon Delos Santos",
                        DateOfBirth = new DateTime(1958, 9, 20),
                        Gender = "Female",
                        Relationship = RelationshipType.Parent,
                        PhilHealthNumber = "04-928173456-1",
                        IdType = "OSCA Senior Citizen ID",
                        IdNumber = "OSCA-TAGUM-2018-4410",
                        StatutoryConsentAgreed = true,
                        CreatedAt = DateTime.UtcNow.AddMonths(-2)
                    }
                );
            }

            db.SaveChanges();
        }
    }
}
