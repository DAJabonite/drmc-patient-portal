using DrmcPatientPortal.Services;

namespace DrmcPatientPortal.Models;

// Availability metadata is distinct from permission to read an attached report.
public class PatientReportCard
{
    public int Id { get; init; }
    public string Source { get; init; } = "lab";
    public string Description { get; init; } = "";
    public string RequestReference { get; init; } = "";
    public DateTime? ExamDate { get; init; }
    public int? ClinicalEncounterId { get; init; }
    public string? EncounterReference { get; init; }
    public DateTime? EncounterDate { get; init; }
    public string Status { get; init; } = "";
    public bool IsReleased { get; init; }
    public bool CanOpenPdf { get; init; }
    public bool HasPdf { get; init; }
    public DateTime ActivityAt { get; init; }
    public LabCategory? Category { get; init; }
    public RadiologyModality? Modality { get; init; }
    public string DetailsUrl => Source == "redstar" ? $"/Patient/LabResults/RedStar/Details/{Id}"
        : Source == "radiology" ? $"/Patient/Radiology/Details/{Id}" : $"/Patient/LabResults/Details/{Id}";
    public string ControllerName => Source == "radiology" ? "Radiology" : "LabResults";
}

public sealed record PatientEncounterReportGroup(int EncounterId, string Reference, DateTime Date,
    IReadOnlyList<PatientReportCard> Reports);

public sealed class PatientReportPage
{
    public IReadOnlyList<PatientEncounterReportGroup> EncounterGroups { get; init; } = [];
    public IReadOnlyList<PatientReportCard> LegacyReports { get; init; } = [];
    public PortalPagination Pagination { get; init; } = new(0, 1);
    public PortalPagination LegacyPagination { get; init; } = new(0, 1, "legacyPage");
    public int RequestCount { get; init; }
    public int PdfCount { get; init; }
    public bool LegacyExcluded { get; init; }
}

public sealed class LabResultsIndexViewModel
{
    public LabCategory? SelectedCategory { get; init; }
    public string? SearchTerm { get; init; }
    public string? SelectedDateRange { get; init; }
    public DateTime? SelectedStartDate { get; init; }
    public DateTime? SelectedEndDate { get; init; }
    public string? SelectedStatus { get; init; }
    public PatientReportPage Results { get; init; } = new();
    public int TotalCount { get; init; }
    public bool HasFilters => SelectedCategory.HasValue || !string.IsNullOrEmpty(SearchTerm)
        || !string.IsNullOrEmpty(SelectedDateRange) || !string.IsNullOrEmpty(SelectedStatus);
}
