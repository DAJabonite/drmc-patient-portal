namespace DrmcPatientPortal.Models;

// Patient pages carry only the metadata needed to select an official PDF.
public sealed class LabPdfReportViewModel
{
    public int Id { get; init; }
    public string TestName { get; init; } = string.Empty;
    public DateTime CollectedAt { get; init; }
}

public sealed record LabReportDateGroup(DateTime Date, IReadOnlyList<LabPdfReportViewModel> Reports);

public sealed class LabResultsIndexViewModel
{
    public LabCategory? SelectedCategory { get; init; }
    public string? SearchTerm { get; init; }
    public string? SelectedDateRange { get; init; }
    public DateTime? SelectedStartDate { get; init; }
    public DateTime? SelectedEndDate { get; init; }
    public IReadOnlyList<LabReportDateGroup> DateGroups { get; init; } = [];
    public int TotalCount { get; init; }
}
