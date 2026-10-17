using System.Security.Cryptography;

namespace Signage.Web.Features.Pairing;

/// <summary>
/// The 6-character code a device shows while waiting to be paired (R-40). The alphabet leaves out characters
/// that are easy to confuse on a TV across a room (0/O, 1/I/L, 2/Z, 5/S, 8/B). 25^6 ≈ 244 million codes.
/// </summary>
public static class PairingCode
{
    public const string Alphabet = "ACDEFGHJKMNPQRTUVWXY34679";
    public const int Length = 6;

    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>How the code is shown: <c>K7P-Q2M</c>.</summary>
    public static string Format(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.Length == Length ? $"{code[..3]}-{code[3..]}" : code;
    }

    /// <summary>What an admin typed, normalised (case, spaces and dashes ignored); null if it can't be a code.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var cleaned = new string(input.Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant).ToArray());
        return cleaned.Length == Length && cleaned.All(c => Alphabet.Contains(c, StringComparison.Ordinal)) ? cleaned : null;
    }
}
