namespace DrmcPatientPortal.Services;

public record ExtractedField<T>(
    T? Value,
    bool Found,
    float Confidence = 1.0f
)
{
    public static ExtractedField<T> Empty() => new(default, false, 0.0f);
    public static ExtractedField<T> From(T? value, float confidence = 1.0f) =>
        value is not null ? new(value, true, confidence) : Empty();
}

public class IdExtractionResult
{
    public bool Success { get; set; }
    public string IdType { get; set; } = string.Empty;
    public float MeanConfidence { get; set; }
    public string? RawTextFront { get; set; }
    public string? RawTextBack { get; set; }

    public ExtractedField<string> FirstName { get; set; } = ExtractedField<string>.Empty();
    public ExtractedField<string> MiddleName { get; set; } = ExtractedField<string>.Empty();
    public ExtractedField<string> LastName { get; set; } = ExtractedField<string>.Empty();
    public ExtractedField<string> FullName { get; set; } = ExtractedField<string>.Empty();
    public ExtractedField<string> IdNumber { get; set; } = ExtractedField<string>.Empty();
    public ExtractedField<DateTime> DateOfBirth { get; set; } = ExtractedField<DateTime>.Empty();
    public ExtractedField<string> Address { get; set; } = ExtractedField<string>.Empty();
    public ExtractedField<string> Sex { get; set; } = ExtractedField<string>.Empty();
    public ExtractedField<string> BloodType { get; set; } = ExtractedField<string>.Empty();

    // Summary of found fields for UI presentation
    public Dictionary<string, string> FoundFieldsSummary
    {
        get
        {
            var dict = new Dictionary<string, string>();
            if (FirstName.Found && !string.IsNullOrWhiteSpace(FirstName.Value)) dict["First name"] = FirstName.Value;
            if (MiddleName.Found && !string.IsNullOrWhiteSpace(MiddleName.Value)) dict["Middle name"] = MiddleName.Value;
            if (LastName.Found && !string.IsNullOrWhiteSpace(LastName.Value)) dict["Last name"] = LastName.Value;
            if (IdNumber.Found && !string.IsNullOrWhiteSpace(IdNumber.Value)) dict["ID number"] = IdNumber.Value;
            if (DateOfBirth.Found && DateOfBirth.Value != default) dict["Date of birth"] = DateOfBirth.Value.ToString("yyyy-MM-dd");
            if (Address.Found && !string.IsNullOrWhiteSpace(Address.Value)) dict["Address"] = Address.Value;
            if (Sex.Found && !string.IsNullOrWhiteSpace(Sex.Value)) dict["Sex"] = Sex.Value;
            if (BloodType.Found && !string.IsNullOrWhiteSpace(BloodType.Value)) dict["Blood type"] = BloodType.Value;
            return dict;
        }
    }
}

public interface IIdDocumentExtractionService
{
    Task<IdExtractionResult> ExtractFromBytesAsync(string idType, byte[] frontImageBytes, byte[]? backImageBytes = null);
    Task<IdExtractionResult> ExtractFromStreamsAsync(string idType, Stream frontImageStream, Stream? backImageStream = null);
}
