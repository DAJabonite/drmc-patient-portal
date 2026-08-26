namespace DrmcPatientPortal.Models;

// Static reference data for DRMC's clinical departments, confirmed current against
// https://drmc.doh.gov.ph/clinical-department/ (Pediatrics, Family & Community Medicine,
// Internal Medicine, Ophthalmology, Surgery, Anesthesiology, OB-Gyne, Radiology).
public record ClinicalDepartment(
    string Name,
    string Description,
    string Icon,
    string HeadOfDepartment = "Department Chair",
    string Location = "OPD Complex",
    string LocalExtension = "Local 101",
    IReadOnlyList<string>? ServicesOffered = null
);

public static class ClinicalDepartments
{
    public static readonly IReadOnlyList<ClinicalDepartment> All = new List<ClinicalDepartment>
    {
        new(
            "Internal Medicine",
            "Diagnoses and treats adult illnesses, from common conditions to complex chronic disease.",
            "stethoscope",
            "Dr. Arthur Llanos, MD, FPCP",
            "OPD Building 1, 2nd Floor, Rooms 201-205",
            "Local 201",
            new[] { "Adult Cardiology", "Pulmonology & Asthma Clinic", "Endocrinology & Diabetes Care", "Gastroenterology", "Nephrology & Renal Care", "Infectious Diseases" }
        ),
        new(
            "Surgery",
            "Provides surgical care across general and specialised procedures for adults and children.",
            "scalpel",
            "Dr. Roberto Valderama, MD, FPCS",
            "OPD Building 2, 1st Floor, Rooms 101-104",
            "Local 202",
            new[] { "General & Laparoscopic Surgery", "Orthopedic & Trauma Surgery", "Pediatric Surgery", "Urologic Surgery", "Plastic & Reconstructive Surgery", "Minor Outpatient Procedures" }
        ),
        new(
            "Pediatrics",
            "Comprehensive care for infants, children, and adolescents, including newborn intensive care.",
            "baby",
            "Dr. Carmela Bautista, MD, FPPS",
            "Pediatric Outpatient Pavilion, Ground Floor",
            "Local 203",
            new[] { "General Pediatric Clinic", "Neonatal Follow-up", "Well-Baby & Immunization", "Pediatric Cardiology", "Pediatric Hematology-Oncology", "Developmental Pediatrics" }
        ),
        new(
            "OB-Gyne",
            "Women's health, pregnancy, and childbirth care.",
            "venus",
            "Dr. Stephanie Joy Garcia, MD, FPOGS",
            "Women's Health Center, 2nd Floor",
            "Local 204",
            new[] { "Prenatal & High-Risk Pregnancy", "General Gynecology", "Gynecologic Oncology", "Reproductive Health & Family Planning", "Perinatology & Fetal Surveillance" }
        ),
        new(
            "Family & Community Medicine",
            "Whole-person, continuing care for individuals and families across the lifespan.",
            "house-user",
            "Dr. Cristina Ramos, MD, FPAFP",
            "Primary Care Pavilion, Ground Floor",
            "Local 205",
            new[] { "PhilHealth Konsulta Primary Care", "Preventive Health & Annual Checkups", "Geriatric Care", "Smoking Cessation Clinic", "Home Care Coordination" }
        ),
        new(
            "Anesthesiology",
            "Safe anaesthesia and peri-operative care for every procedure.",
            "syringe",
            "Dr. Victorino Lim, MD, FPSA",
            "Surgical Complex, 3rd Floor",
            "Local 206",
            new[] { "Pre-Anesthesia Evaluation Clinic", "Acute & Chronic Pain Management", "Epidural & Obstetric Analgesia", "Critical Care Consultation" }
        ),
        new(
            "Ophthalmology",
            "Eye and vision care, from routine checks to surgical treatment.",
            "eye",
            "Dr. Ramon Guingona, MD, FPAO",
            "Eye Care Center, OPD Building 1, 3rd Floor",
            "Local 207",
            new[] { "Comprehensive Eye Exam", "Cataract Evaluation & Surgery", "Glaucoma Screening & Management", "Diabetic Retinopathy Screening", "Refraction & Optical Services" }
        ),
        new(
            "Radiology",
            "Medical imaging — X-ray, ultrasound, CT, and MRI — to guide diagnosis.",
            "scan",
            "Dr. Francis Xavier Gomez, MD, FPCR",
            "Diagnostic Imaging Center, Basement 1",
            "Local 208",
            new[] { "Digital X-Ray & Fluoroscopy", "High-Resolution Ultrasound & Doppler", "128-Slice Multi-detector CT Scan", "1.5T Magnetic Resonance Imaging (MRI)", "Digital Mammography" }
        ),
    };

    public static readonly string Heading = "Clinical Departments";
    public static readonly string Subheading = "DRMC specialities and services";
}
