namespace DrmcPatientPortal.Models;

// Typed outpatient facility wayfinding projections shared by the public guide and defaults adapter.
public record OpdGuideStep(
    int StepNumber,
    string Title,
    string? Location = null,
    string? RequirementNote = null,
    string? Detail = null,
    IReadOnlyList<string>? Branches = null,
    bool IsConditional = false,
    IReadOnlyList<string>? Destinations = null
);

public record OpdFacilityFlow(
    string Key,
    string Name,
    string ShortName,
    OpdGuideStep? PreRegistration,
    IReadOnlyList<OpdGuideStep> Steps,
    string? VerificationNote = null
);

// Compatibility API for callers that need the bundled English reference data.
// Public pages resolve localized revisions through InstitutionalGuideService.
public static class OpdGuideFlows
{
    public static readonly IReadOnlyList<OpdFacilityFlow> All =
        Services.InstitutionalGuideDefaults.Get("opd").Opd!.Facilities
            .Select(facility => facility.ToFlow()).ToArray();
}
