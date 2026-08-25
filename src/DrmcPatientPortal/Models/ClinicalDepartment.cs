namespace DrmcPatientPortal.Models;

// Static reference data for DRMC's clinical departments, confirmed current against
// https://drmc.doh.gov.ph/clinical-department/ (Pediatrics, Family & Community Medicine,
// Internal Medicine, Ophthalmology, Surgery, Anesthesiology, OB-Gyne, Radiology).
public record ClinicalDepartment(string Name, string Description, string Icon);

public static class ClinicalDepartments
{
    public static readonly IReadOnlyList<ClinicalDepartment> All = new List<ClinicalDepartment>
    {
        new("Internal Medicine", "Diagnoses and treats adult illnesses, from common conditions to complex chronic disease.", "stethoscope"),
        new("Surgery", "Provides surgical care across general and specialised procedures for adults and children.", "scalpel"),
        new("Pediatrics", "Comprehensive care for infants, children, and adolescents, including newborn intensive care.", "baby"),
        new("OB-Gyne", "Women's health, pregnancy, and childbirth care.", "venus"),
        new("Family & Community Medicine", "Whole-person, continuing care for individuals and families across the lifespan.", "house-user"),
        new("Anesthesiology", "Safe anaesthesia and peri-operative care for every procedure.", "syringe"),
        new("Ophthalmology", "Eye and vision care, from routine checks to surgical treatment.", "eye"),
        new("Radiology", "Medical imaging — X-ray, ultrasound, CT, and MRI — to guide diagnosis.", "scan"),
    };

    public static readonly string Heading = "Clinical Departments";
    public static readonly string Subheading = "DRMC specialities and services";
}
