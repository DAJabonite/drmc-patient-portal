using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public sealed class SingleUseAuthenticatorProvider(IServiceScopeFactory scopes, TimeProvider clock)
    : AuthenticatorTokenProvider<ApplicationUser>
{
    public const string ProviderName = "SingleUseAuthenticator";

    public override async Task<bool> ValidateAsync(string purpose, string token, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var key = await manager.GetAuthenticatorKeyAsync(user);
        if (key is null || token.Length != 6 || token.Any(c => c is < '0' or > '9')) return false;
        var step = clock.GetUtcNow().ToUnixTimeSeconds() / 30;
        for (var offset = -2; offset <= 2; offset++)
        {
            var candidate = step + offset;
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(token), Encoding.ASCII.GetBytes(Code(key, candidate)))) continue;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AuthenticatorConsumptions.Add(new()
            {
                UserId = user.Id, KeyFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))),
                TimeStep = candidate, ExpiresUtc = DateTimeOffset.FromUnixTimeSeconds((candidate + 3) * 30)
            });
            // The database key arbitrates across replicas and purposes. Never use a cache check
            // or turn an uncertain database write into a successful authentication.
            try { await db.SaveChangesAsync(); return true; }
            catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 }) { return false; }
        }
        return false;
    }

    public static string Code(string base32Key, long timeStep)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var bits = 0;
        var buffer = 0;
        foreach (var character in base32Key.TrimEnd('=').ToUpperInvariant())
        {
            var value = alphabet.IndexOf(character);
            if (value < 0) throw new ArgumentException("Invalid authenticator key.", nameof(base32Key));
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); }
        }
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, timeStep);
        var hash = HMACSHA1.HashData(bytes.ToArray(), counter);
        var offset = hash[^1] & 15;
        var number = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & int.MaxValue;
        return (number % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }
}
