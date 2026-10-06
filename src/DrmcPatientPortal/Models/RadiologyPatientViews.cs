namespace DrmcPatientPortal.Models;

// Patient-facing projections of RadiologyStudy. They never carry InternalNotes, and report text is
// only present when PatientResultsDisclosure says the report is released and D4 is switched on.
public sealed class RadiologyListItem
{
    public int Id { get; init; }
    public string AccessionNumber { get; init; } = string.Empty;
    public string StudyName { get; init; } = string.Empty;
    public RadiologyModality Modality { get; init; }
    public string BodyRegion { get; init; } = string.Empty;
    public DateTime PerformedAt { get; init; }
    public DateTime? ReleasedAt { get; init; }   // only set once released
    public string OrderingPhysician { get; init; } = string.Empty;
    public bool IsReady { get; init; }
}

public sealed class RadiologyIndexViewModel
{
    public RadiologyModality? SelectedModality { get; init; }
    public string? SearchTerm { get; init; }
    public string? SelectedDateRange { get; init; }
    public DateTime? SelectedStartDate { get; init; }
    public DateTime? SelectedEndDate { get; init; }
    public IReadOnlyList<RadiologyListItem> Studies { get; init; } = Array.Empty<RadiologyListItem>();
    public int TotalCount { get; init; }
    public int ShownReady => Studies.Count(s => s.IsReady);
    public int ShownInProgress => Studies.Count(s => !s.IsReady);
    public bool HasFilters => SelectedModality.HasValue || !string.IsNullOrEmpty(SearchTerm) || !string.IsNullOrEmpty(SelectedDateRange);
    public ClinicalDepartment Department { get; init; } = ClinicalDepartments.Radiology;
}

public sealed class RadiologyReportView
{
    public string ClinicalIndication { get; init; } = string.Empty;
    public string Technique { get; init; } = string.Empty;
    public string Comparison { get; init; } = string.Empty;
    public string Findings { get; init; } = string.Empty;
    public string Impression { get; init; } = string.Empty;
    public string PlainLanguageSummary { get; init; } = string.Empty;
    public string AmendmentNote { get; init; } = string.Empty;
}

public sealed class RadiologyDetailsViewModel
{
    public int Id { get; init; }
    public string AccessionNumber { get; init; } = string.Empty;
    public string StudyName { get; init; } = string.Empty;
    public RadiologyModality Modality { get; init; }
    public string BodyRegion { get; init; } = string.Empty;
    public DateTime PerformedAt { get; init; }
    public DateTime? ReleasedAt { get; init; }   // only set once released
    public string OrderingPhysician { get; init; } = string.Empty;
    public string RadiologistName { get; init; } = string.Empty; // only set once released
    public string PerformingUnit { get; init; } = string.Empty;
    public bool IsReady { get; init; }
    public bool IsAmended { get; init; }          // only true once released
    public RadiologyReportView? Report { get; init; }
    public ClinicalDepartment Department { get; init; } = ClinicalDepartments.Radiology;
}
