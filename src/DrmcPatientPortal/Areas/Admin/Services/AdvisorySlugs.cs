using System.Globalization;
using System.Text;
using DrmcPatientPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public static class AdvisorySlugs
{
    public static string Stem(string title)
    {
        var output = new StringBuilder(400);
        var separator = false;
        foreach (var character in title.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
            if (character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                if (separator && output.Length > 0) output.Append('-');
                if (output.Length == 400) break;
                output.Append(char.ToLowerInvariant(character));
                separator = false;
            }
            else separator = output.Length > 0;
            if (output.Length == 400) break;
        }
        var stem = output.ToString().TrimEnd('-');
        return stem.Length == 0 ? "advisory" : stem;
    }

    // Called within AdminWrites' serializable transaction. Update/range locks reserve the
    // candidate namespace until the advisory and its required audit have committed.
    public static async Task<string> CreateAsync(ApplicationDbContext db, string title, CancellationToken token)
    {
        var stem = Stem(title);
        var prefix = stem + "-%";
        var existing = await db.Database.SqlQuery<string>($"""
            SELECT [Slug] AS [Value] FROM [PublicAdvisories] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Slug] COLLATE Latin1_General_100_CI_AS = {stem}
               OR [Slug] COLLATE Latin1_General_100_CI_AS LIKE {prefix}
            """).ToListAsync(token);
        var used = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(stem)) return stem;
        for (var suffix = 2; ; suffix = checked(suffix + 1))
        {
            var candidate = stem + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(candidate)) return candidate;
        }
    }
}
