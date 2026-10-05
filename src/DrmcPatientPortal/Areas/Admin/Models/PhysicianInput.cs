using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DrmcPatientPortal.Areas.Admin.Models;

public enum PhysicianSource { Directory, HistoricalName }
public interface IPhysicianChoice
{
    PhysicianSource PhysicianSource { get; set; }
    int? DirectoryDoctorId { get; set; }
    string? HistoricalDoctorName { get; set; }
}
public abstract class PhysicianInput : AdminInput, IValidatableObject, IPhysicianChoice
{
    [EnumDataType(typeof(PhysicianSource)), Display(Name = "Physician source")]
    public PhysicianSource PhysicianSource { get; set; } = PhysicianSource.HistoricalName;
    [Display(Name = "Directory physician")]
    public int? DirectoryDoctorId { get; set; }
    [StringLength(100000), Display(Name = "Historical physician name")]
    public string? HistoricalDoctorName { get; set; }
    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => ValidateChoice(this);
    public static IEnumerable<ValidationResult> ValidateChoice(IPhysicianChoice input)
    {
        if (input.PhysicianSource == PhysicianSource.Directory && input.DirectoryDoctorId is null)
            yield return new ValidationResult("Select a directory physician.", [nameof(DirectoryDoctorId)]);
        if (input.PhysicianSource == PhysicianSource.HistoricalName && string.IsNullOrWhiteSpace(input.HistoricalDoctorName))
            yield return new ValidationResult("Enter the historical physician name.", [nameof(HistoricalDoctorName)]);
        if (input.PhysicianSource == PhysicianSource.HistoricalName && input.DirectoryDoctorId is not null)
            yield return new ValidationResult("Use either the directory physician or the historical name, not both.", [nameof(DirectoryDoctorId)]);
        if (input.PhysicianSource == PhysicianSource.Directory && !string.IsNullOrWhiteSpace(input.HistoricalDoctorName))
            yield return new ValidationResult("Use either the directory physician or the historical name, not both.", [nameof(HistoricalDoctorName)]);
    }
}
public sealed class PhysicianChoice : IPhysicianChoice, IValidatableObject
{
    [EnumDataType(typeof(PhysicianSource))] public PhysicianSource PhysicianSource { get; set; } = PhysicianSource.HistoricalName;
    public int? DirectoryDoctorId { get; set; }
    [StringLength(100000)] public string? HistoricalDoctorName { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => PhysicianInput.ValidateChoice(this);
}
public sealed record PhysicianForm(IPhysicianChoice Input, IReadOnlyList<SelectListItem> Doctors, string Prefix = "");
