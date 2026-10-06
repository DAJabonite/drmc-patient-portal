namespace DrmcPatientPortal.Models;

// Patient-facing labels for enum values; never render enum identifiers directly.
public static class CategoryDisplayNames
{
    public static string DisplayName(this LabCategory category) => category switch
    {
        LabCategory.Hematology => "Hematology",
        LabCategory.ClinicalChemistry => "Clinical Chemistry",
        LabCategory.Microbiology => "Microbiology",
        LabCategory.UrinalysisFecalysis => "Urinalysis & Fecalysis",
        LabCategory.RadiologyImaging => "Radiology & Imaging",
        LabCategory.SpecialDiagnostics => "Special Diagnostics",
        _ => category.ToString()
    };

    public static string DisplayName(this RadiologyModality modality) => modality switch
    {
        RadiologyModality.XRay => "X-ray",
        RadiologyModality.Fluoroscopy => "Fluoroscopy",
        RadiologyModality.Ultrasound => "Ultrasound",
        RadiologyModality.CT => "CT scan",
        RadiologyModality.MRI => "MRI",
        RadiologyModality.Mammography => "Mammography",
        RadiologyModality.Other => "Other imaging",
        _ => "Imaging"
    };

    public static string DisplayName(this RadiologyStatus status) => status switch
    {
        RadiologyStatus.InProgress => "In progress",
        RadiologyStatus.PendingVerification => "Pending verification",
        RadiologyStatus.Final => "Final",
        RadiologyStatus.Amended => "Amended",
        _ => "Unknown"
    };

    public static string DisplayName(this AdvisoryCategory category) => category switch
    {
        AdvisoryCategory.HealthAlert => "Health Alert",
        AdvisoryCategory.Vaccination => "Vaccination",
        AdvisoryCategory.HospitalNotice => "Hospital Notice",
        AdvisoryCategory.SeasonalHealth => "Seasonal Health",
        AdvisoryCategory.Advisory => "Advisory",
        _ => category.ToString()
    };
}
