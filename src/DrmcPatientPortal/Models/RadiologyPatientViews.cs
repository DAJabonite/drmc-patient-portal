namespace DrmcPatientPortal.Models;

// Only PDF metadata is projected onto patient pages; clinical text stays in the report.
public sealed class RadiologyListItem
{
    public int Id { get; init; }
    public RadiologyModality Modality { get; init; }
    public DateTime PerformedAt { get; init; }
    public string ModalityLabel => Modality.PdfDisplayName();
}

public sealed record RadiologyDateGroup(DateTime Date, IReadOnlyList<RadiologyListItem> Studies);

public sealed class RadiologyIndexViewModel
{
    public required ResolvedInstitutionalGuide PreparationGuide { get; init; }
    public RadiologyModality? SelectedModality { get; init; }
    public string? SearchTerm { get; init; }
    public string? SelectedDateRange { get; init; }
    public DateTime? SelectedStartDate { get; init; }
    public DateTime? SelectedEndDate { get; init; }
    public IReadOnlyList<RadiologyDateGroup> DateGroups { get; init; } = [];
    public int TotalCount { get; init; }
    public bool HasFilters => SelectedModality.HasValue || !string.IsNullOrEmpty(SearchTerm) || !string.IsNullOrEmpty(SelectedDateRange);
    public ClinicalDepartment Department { get; init; } = ClinicalDepartments.Radiology;
}

public sealed class RadiologyDetailsViewModel
{
    public int Id { get; init; }
    public RadiologyModality Modality { get; init; }
    public DateTime PerformedAt { get; init; }
    public string ModalityLabel => Modality.PdfDisplayName();
}
