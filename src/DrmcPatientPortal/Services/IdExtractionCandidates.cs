namespace DrmcPatientPortal.Services;

/// <summary>Combines passes for one document only; empty retries never erase a recovered field.</summary>
internal static class IdExtractionCandidates
{
    public static void Merge(IdExtractionResult target, IdExtractionResult candidate, float confidence, float? addressConfidence = null)
    {
        confidence = Math.Clamp(confidence, 0, 1);
        MergeNames();
        target.IdNumber = Best(target.IdNumber, candidate.IdNumber);
        target.DateOfBirth = Best(target.DateOfBirth, candidate.DateOfBirth);
        target.Address = Best(target.Address, candidate.Address, addressConfidence);
        target.Sex = Best(target.Sex, candidate.Sex);
        target.BloodType = Best(target.BloodType, candidate.BloodType);
        var name = string.Join(" ", new[] { target.FirstName.Value, target.MiddleName.Value, target.LastName.Value }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        target.FullName = name.Length == 0 ? ExtractedField<string>.Empty() : ExtractedField<string>.From(name);
        target.Success = target.FoundFieldsSummary.Count > 0;

        void MergeNames()
        {
            if (confidence < .35f) return;
            var complete = candidate.FirstName.Found && candidate.LastName.Found;
            var currentComplete = target.FirstName.Found && target.LastName.Found;
            if (complete)
            {
                var currentConfidence = Math.Min(target.FirstName.Confidence, target.LastName.Confidence);
                var better = !currentComplete || candidate.NameEvidence > target.NameEvidence ||
                    (candidate.NameEvidence == target.NameEvidence && confidence > currentConfidence + .02f);
                var sameNames = candidate.FirstName.Value == target.FirstName.Value && candidate.LastName.Value == target.LastName.Value;
                if (better)
                {
                    target.FirstName = candidate.FirstName with { Confidence = confidence };
                    target.LastName = candidate.LastName with { Confidence = confidence };
                    // A middle name is tied to its given/family name pair, not an independent vote.
                    target.MiddleName = candidate.MiddleName.Found ? candidate.MiddleName with { Confidence = confidence }
                        : sameNames ? target.MiddleName : ExtractedField<string>.Empty();
                    target.NameEvidence = candidate.NameEvidence;
                }
                else if (sameNames && !target.MiddleName.Found && candidate.MiddleName.Found &&
                    candidate.NameEvidence >= target.NameEvidence)
                    target.MiddleName = candidate.MiddleName with { Confidence = confidence };
                return;
            }
            // Cropped retries may fill absent components, but cannot truncate a coherent name.
            var compatible = (!candidate.FirstName.Found || !target.FirstName.Found || candidate.FirstName.Value == target.FirstName.Value) &&
                (!candidate.LastName.Found || !target.LastName.Found || candidate.LastName.Value == target.LastName.Value);
            if (currentComplete)
            {
                if (compatible && !target.MiddleName.Found && candidate.MiddleName.Found &&
                    candidate.NameEvidence >= target.NameEvidence)
                    target.MiddleName = candidate.MiddleName with { Confidence = confidence };
                return;
            }
            if (!compatible) return;
            target.FirstName = Best(target.FirstName, candidate.FirstName);
            target.MiddleName = Best(target.MiddleName, candidate.MiddleName);
            target.LastName = Best(target.LastName, candidate.LastName);
            if (target.FirstName.Found && target.LastName.Found) target.NameEvidence = candidate.NameEvidence;
        }

        ExtractedField<T> Best<T>(ExtractedField<T> current, ExtractedField<T> next, float? fieldConfidence = null)
        {
            var score = Math.Clamp(fieldConfidence ?? confidence, 0, 1);
            return next.Found && score >= .35f && (!current.Found || score > current.Confidence + .02f)
                ? next with { Confidence = score } : current;
        }
    }
}
