using System.Security.Cryptography;
using System.Text;

namespace Backend.Domain;

/// <summary>
/// The secret in a patient's "manage my appointment" link. Only its SHA-256 hash is stored, so a database leak does not
/// hand out working links. Patients have no account; the link is their key.
/// </summary>
public static class ManageToken
{
    public static string Create() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>Rejects obvious junk before it reaches the database.</summary>
    public static bool LooksValid(string? token) =>
        token is { Length: 32 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
