namespace DrmcPatientPortal.Services;

/// <summary>Combines passes for one document only; empty retries never erase a recovered field.</summary>
internal static class IdExtractionCandidates
{
    public static void Merge(IdExtractionResult target, IdExtractionResult candidate, float confidence, float? addressConfidence = null)
    {
        confidence = Math.Clamp(confidence, 0, 1);
        target.FirstName = Best(target.FirstName, candidate.FirstName);
        target.MiddleName = Best(target.MiddleName, candidate.MiddleName);
        target.LastName = Best(target.LastName, candidate.LastName);
        target.IdNumber = Best(target.IdNumber, candidate.IdNumber);
        target.DateOfBirth = Best(target.DateOfBirth, candidate.DateOfBirth);
        target.Address = Best(target.Address, candidate.Address, addressConfidence);
        target.Sex = Best(target.Sex, candidate.Sex);
        target.BloodType = Best(target.BloodType, candidate.BloodType);
        var name = string.Join(" ", new[] { target.FirstName.Value, target.MiddleName.Value, target.LastName.Value }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        target.FullName = name.Length == 0 ? ExtractedField<string>.Empty() : ExtractedField<string>.From(name);
        target.Success = target.FoundFieldsSummary.Count > 0;

        ExtractedField<T> Best<T>(ExtractedField<T> current, ExtractedField<T> next, float? fieldConfidence = null)
        {
            var score = Math.Clamp(fieldConfidence ?? confidence, 0, 1);
            return next.Found && score >= .35f && (!current.Found || score > current.Confidence + .02f)
                ? next with { Confidence = score } : current;
        }
    }
}
