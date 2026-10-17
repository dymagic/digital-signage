using System.Security.Cryptography;
using System.Text;

namespace Signage.Web.Features.Pairing;

/// <summary>
/// Screen tokens and device secrets: 256-bit random values (R-53). Only their SHA-256 hash is stored, so a
/// database leak doesn't hand out working tokens.
/// </summary>
public static class ScreenTokens
{
    public static string NewSecret() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    /// <summary>Compares a secret with a stored hash in constant time.</summary>
    public static bool Matches(string? secret, string? storedHash) =>
        secret is not null && storedHash is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(secret)), Encoding.ASCII.GetBytes(storedHash));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
