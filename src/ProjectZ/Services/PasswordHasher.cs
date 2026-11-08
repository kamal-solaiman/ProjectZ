using System.Globalization;
using System.Security.Cryptography;

namespace ProjectZ.Services;

// Format: PBKDF2-SHA256$iterations$base64(salt)$base64(hash)
// Also accepts legacy PBKDF2SHA256$iterations$base64(salt)$base64(hash) (>= 100,000 iterations)
public static class PasswordHasher
{
    public const int Iterations = 600_000;
    public const int MinVerifyIterations = 100_000;
    public const int MaxVerifyIterations = 2_000_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2-SHA256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        try
        {
            var parts = encoded.Split('$');
            if (parts.Length != 4 ||
                parts[0] is not ("PBKDF2-SHA256" or "PBKDF2SHA256") ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) ||
                iterations < MinVerifyIterations || iterations > MaxVerifyIterations) return false;
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != 16 || expected.Length != 32) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
