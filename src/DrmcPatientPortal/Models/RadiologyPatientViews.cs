namespace DrmcPatientPortal.Models;

public sealed class RadiologyIndexViewModel
{
    public required ResolvedInstitutionalGuide PreparationGuide { get; init; }
    public RadiologyModality? SelectedModality { get; init; }
    public string? SearchTerm { get; init; }
    public string? SelectedDateRange { get; init; }
    public DateTime? SelectedStartDate { get; init; }
    public DateTime? SelectedEndDate { get; init; }
    public string? SelectedStatus { get; init; }
    public PatientReportPage Results { get; init; } = new();
    public int TotalCount { get; init; }
    public bool HasFilters => SelectedModality.HasValue || !string.IsNullOrEmpty(SearchTerm)
        || !string.IsNullOrEmpty(SelectedDateRange) || !string.IsNullOrEmpty(SelectedStatus);
    public ClinicalDepartment Department { get; init; } = ClinicalDepartments.Radiology;
}
