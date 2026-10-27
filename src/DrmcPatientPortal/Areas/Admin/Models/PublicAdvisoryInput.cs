using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class PublicAdvisoryInput : AdminInput, IValidatableObject
{
    [Required, StringLength(100000)] public string Title { get; set; } = "";
    [StringLength(450), RegularExpression("[a-zA-Z0-9]+(?:-[a-zA-Z0-9]+)*", ErrorMessage = "Use letters, digits and single hyphens for the slug.")]
    public string Slug { get; set; } = "";
    [EnumDataType(typeof(AdvisoryCategory))] public AdvisoryCategory Category { get; set; }
    [EnumDataType(typeof(AdvisoryPriority))] public AdvisoryPriority Priority { get; set; }
    [Required, StringLength(100000)] public string Summary { get; set; } = "";
    [Required, StringLength(100000), Display(Name = "Content HTML")] public string ContentHtml { get; set; } = "";
    [BindNever, StringLength(100000)] public string? ImageUrl { get; set; }
    [Display(Name = "Advisory image")] public IFormFile? ImageUpload { get; set; }
    [Display(Name = "Remove current image")] public bool RemoveImage { get; set; }
    [Required, StringLength(100000), Display(Name = "Issuing unit")] public string IssuingUnit { get; set; } = "";
    [Required, Display(Name = "Published at (UTC)")] public DateTime? PublishedAt { get; set; } = DateTime.UtcNow;
    [Display(Name = "Effective until (UTC)")] public DateTime? EffectiveUntil { get; set; }
    [Display(Name = "Pinned")] public bool IsPinned { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (EffectiveUntil < PublishedAt) yield return new ValidationResult("Effective until cannot precede publication.", [nameof(EffectiveUntil)]);
        if (RemoveImage && ImageUpload is not null)
            yield return new ValidationResult("Choose a replacement image or remove the current image, not both.", [nameof(ImageUpload)]);
        if (!AdvisoryHtml.SafeUrl(ImageUrl)) yield return new ValidationResult("Use an HTTPS or local relative image URL.", [nameof(ImageUrl)]);
    }
}
