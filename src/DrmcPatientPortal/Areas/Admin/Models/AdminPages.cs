using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public interface IAdminInput
{
    string? RowVersion { get; set; }
}

public abstract class AdminInput : IAdminInput
{
    [StringLength(12)] public string? RowVersion { get; set; }
}

public sealed record OwnershipContext(PatientRecord Patient, string Label, int RouteId);
public sealed record AdminRow(int Id, string Label, string Summary, int? PatientId);
public sealed record RecordField(string Label, string Value);
public sealed record AdminList(string Title, IReadOnlyList<AdminRow> Rows, string? Search, int Page, int Total, int? ContextId = null, OwnershipContext? Context = null)
{
    public int Pages => Math.Max(1, (Total + 24) / 25);
}
public sealed record AdminForm(string Title, object Input, string FormPartial, int? Id = null, OwnershipContext? Context = null);
public sealed record AdminDetails(string Title, int Id, IReadOnlyList<RecordField> Fields, string RowVersion,
    OwnershipContext? Context, IReadOnlyDictionary<string, int> Dependents);
