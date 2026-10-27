using System.Security.Cryptography;
using System.Text;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

// New codes contain 20 Crockford base32 characters (100 random bits), in four groups of five.
// Previously issued 15-character codes remain acceptable until their normal expiry.
// Input is not case-sensitive and ignores spaces and hyphens; O, I and L are read as 0, 1 and 1.
public static class HospitalRecordCodes
{
    public const int Length = 20, LegacyLength = 15, GroupLength = 5;
    public const int DisplayLength = Length + Length / GroupLength - 1;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const string FragmentKey = "code";
    public const string LinkError = "We could not verify the hospital record and portal account.";

    // Every identity factor must match. A legacy record without an MRN must be repaired first.
    public static bool MatchesIdentity(PatientRecord record, string? lastName, string? firstName, DateTime? birthDate,
        string? middleName = null, string? suffix = null) =>
        record.HasHospitalNumber && birthDate is not null && record.DateOfBirth is not null
        && record.DateOfBirth.Value.Date == birthDate.Value.Date
        && PatientNames.Matches(record, lastName, firstName, middleName, suffix);

    // MRNs retain leading zeros and punctuation; only surrounding whitespace and case vary.
    public static bool MatchesRecord(PatientRecord record, string? hospitalNumber, string? lastName, string? firstName,
        DateTime? birthDate, string? middleName = null, string? suffix = null) =>
        !string.IsNullOrWhiteSpace(hospitalNumber) && record.HasHospitalNumber
        && string.Equals(record.HospitalNumber!.Trim(), hospitalNumber.Trim(), StringComparison.OrdinalIgnoreCase)
        && MatchesIdentity(record, lastName, firstName, birthDate, middleName, suffix);

    // Call only outside the rejected linking/signup transaction. The attempt and required audit
    // commit together, and concurrent failures increment the database value rather than a snapshot.
    public static async Task RejectIdentityMismatchAsync(ApplicationDbContext db, IAuditLogService audit,
        HttpContext context, int codeId, string reason, string resource = "PatientRegistrationCode")
    {
        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        await db.PatientRegistrationCodes.Where(c => c.Id == codeId && c.RedeemedAtUtc == null && c.RevokedAtUtc == null
                && c.LockedAtUtc == null && c.FailedAttemptCount < PatientRegistrationCode.MaximumFailedAttempts
                && c.ExpiresAtUtc > now && c.PatientRecord.PortalUserId == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.LockedAtUtc, c => c.FailedAttemptCount == PatientRegistrationCode.MaximumFailedAttempts - 1 ? (DateTime?)now : null)
                .SetProperty(c => c.FailedAttemptCount, c => c.FailedAttemptCount + 1), CancellationToken.None);
        await HospitalRecordLinkAudit.RejectedAsync(audit, context, reason, resource);
        await transaction.CommitAsync(CancellationToken.None);
    }

    public static string Generate()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return Format(new string(chars));
    }

    // Returns the canonical current or legacy form, without truncating invalid extra characters.
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 64) return null;
        var builder = new StringBuilder(Length);
        foreach (var raw in input)
        {
            if (raw is ' ' or '-' or '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\t') continue;
            var c = char.ToUpperInvariant(raw) switch { 'O' => '0', 'I' or 'L' => '1', var other => other };
            if (Alphabet.IndexOf(c) < 0) return null;
            builder.Append(c);
        }
        return builder.Length is Length or LegacyLength ? builder.ToString() : null;
    }

    public static string Format(string canonical)
    {
        if (canonical.Length is not (Length or LegacyLength)) throw new ArgumentException("Invalid code length.", nameof(canonical));
        return string.Join('-', Enumerable.Range(0, canonical.Length / GroupLength).Select(i => canonical.Substring(i * GroupLength, GroupLength)));
    }

    public static byte[] Hash(string canonical) => SHA256.HashData(Encoding.ASCII.GetBytes(canonical));

    public static string Hint(string canonical) => canonical[^GroupLength..];

    // Absolute signup URL for the QR. The code travels in the fragment, which browsers never send
    // to the server, so it does not reach request logs or referrers.
    public static string SignupUrl(string registerPageUrl, string formattedCode) => $"{registerPageUrl}#{FragmentKey}={formattedCode}";
}

public sealed class PatientRegistrationOptions
{
    public const string Section = "PatientRegistration";
    // On by default: portal accounts are only for patients who already have a DRMC hospital record.
    // Only set false for a supervised rollout where staff link accounts manually afterwards.
    public bool RequireHospitalRecordCode { get; set; } = true;
    public int CodeLifetimeDays { get; set; } = 7;
    public TimeSpan CodeLifetime => TimeSpan.FromDays(Math.Clamp(CodeLifetimeDays, 1, 30));
}
