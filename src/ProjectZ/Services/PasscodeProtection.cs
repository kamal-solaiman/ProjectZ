using System.Security.Cryptography;
using System.Text;

namespace ProjectZ.Services;

/// <summary>
/// Protects the device passcode written to <c>dbo.MaintenanceOrders.PasscodeValueProtected</c>.
/// <para>
/// The key is never stored in the database: it is read from the <c>PROJECTZ_PASSCODE_KEY</c>
/// environment variable or from <c>PasscodeKey</c> in the external <c>connectionsettings.json</c>
/// (base64, 32 bytes). Stored values look like <c>enc:v1:</c> followed by
/// base64(nonce[12] + ciphertext + tag[16]) using AES-GCM.
/// </para>
/// <para>
/// When no key is configured the passcode is written unchanged (the behaviour of earlier builds) and
/// the intake screen shows a warning instead of pretending the value is protected. A value that was
/// encrypted with a different key is never returned as garbage: it is replaced by a readable
/// placeholder so the receipt and the details window stay usable.
/// </para>
/// </summary>
public sealed class PasscodeProtector
{
    public const string Prefix = "enc:v1:";

    /// <summary>Shown instead of a value that is encrypted while this machine has no key.</summary>
    public const string MissingKeyPlaceholder = "(رمز محمي — مفتاح التشفير غير مُهيّأ على هذا الجهاز)";

    /// <summary>Shown instead of a value that cannot be decrypted with the configured key.</summary>
    public const string UnreadablePlaceholder = "(تعذّر فك تشفير رمز الجهاز — مفتاح التشفير مختلف)";

    /// <summary>Longest passcode the intake form accepts; keeps the encrypted payload inside NVARCHAR(1000).</summary>
    public const int MaxPlainLength = 100;

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[]? key;

    public PasscodeProtector(byte[]? key)
    {
        this.key = key is { Length: 32 } ? key : null;
    }

    public bool IsEnabled => key is not null;

    /// <summary>Encrypts a non-empty passcode; returns the value unchanged when protection is off.</summary>
    public string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || key is null) return value ?? "";

        var plain = Encoding.UTF8.GetBytes(value);
        var payload = new byte[NonceSize + plain.Length + TagSize];
        var nonce = payload.AsSpan(0, NonceSize);
        var cipher = payload.AsSpan(NonceSize, plain.Length);
        var tag = payload.AsSpan(NonceSize + plain.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        return Prefix + Convert.ToBase64String(payload);
    }

    /// <summary>Decrypts a stored passcode. Never throws: unreadable values become a readable placeholder.</summary>
    public string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
        if (key is null) return MissingKeyPlaceholder;

        try
        {
            var payload = Convert.FromBase64String(stored[Prefix.Length..]);
            if (payload.Length < NonceSize + TagSize) return UnreadablePlaceholder;

            var nonce = payload.AsSpan(0, NonceSize);
            var cipher = payload.AsSpan(NonceSize, payload.Length - NonceSize - TagSize);
            var tag = payload.AsSpan(payload.Length - TagSize, TagSize);
            var plain = new byte[cipher.Length];

            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);

            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return UnreadablePlaceholder;
        }
    }
}
