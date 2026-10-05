using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class DoseInput : AdminInput, IValidatableObject
{
    private static readonly string[] Formats = ["HH:mm", "HH:mm:ss", "HH:mm:ss.FFFFFFF"];
    [Required, StringLength(16), Display(Name = "Dose time (Manila)")] public string DoseTime { get; set; } = "";
    [Required, Range(0, int.MaxValue), Display(Name = "Display order")] public int? DisplayOrder { get; set; }
    public static TimeOnly? Parse(string? value) => TimeOnly.TryParseExact(value, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Parse(DoseTime) is null) yield return new ValidationResult("Enter a valid Manila time without a time-zone suffix.", [nameof(DoseTime)]);
    }
}
