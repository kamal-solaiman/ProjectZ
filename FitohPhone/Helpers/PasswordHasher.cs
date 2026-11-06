using System.Security.Cryptography;

namespace FitohPhone.Helpers;

/// <summary>
/// PBKDF2-SHA256 password hashing (Rfc2898DeriveBytes).
/// Stored format: PBKDF2SHA256$iterations$saltBase64$hashBase64
/// Never stores the plain-text password.
/// </summary>
public static class PasswordHasher
{
    private const string AlgorithmPrefix = "PBKDF2SHA256";
    private const int SaltSize = 16;          // 128-bit salt
    private const int HashSize = 32;          // 256-bit hash
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return $"{AlgorithmPrefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(storedHash);

        string[] parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != AlgorithmPrefix)
            return false;

        if (!int.TryParse(parts[1], out int iterations) || iterations <= 0)
            return false;

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
