using System.Security.Cryptography;

namespace Signage.Web.Features.Users;

/// <summary>Length-based password rules (NIST SP 800-63B style: long, no composition rules).</summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    // No 0/O, 1/l/I: temporary passwords are read aloud or copied from a screen.
    private const string TemporaryAlphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string? Validate(string? password) => password switch
    {
        null or { Length: < MinLength } => AccountErrors.PasswordTooShort,
        { Length: > MaxLength } => AccountErrors.PasswordTooLong,
        _ => null,
    };

    /// <summary>A random 16-character password for new or reset users; they must change it at first login.</summary>
    public static string GenerateTemporary() => RandomNumberGenerator.GetString(TemporaryAlphabet, 16);
}
