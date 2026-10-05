using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class LabItemInput : AdminInput
{
    [Required, StringLength(100000), Display(Name = "Parameter name")] public string ParameterName { get; set; } = "";
    [StringLength(100000)] public string? Value { get; set; }
    [StringLength(100000)] public string? Unit { get; set; }
    [StringLength(100000), Display(Name = "Reference range")] public string? ReferenceRange { get; set; }
    [EnumDataType(typeof(LabFlag))] public LabFlag Flag { get; set; }
}
