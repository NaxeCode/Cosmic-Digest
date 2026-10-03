using System.Security.Cryptography;
using System.Text;

public static class DurableSecretProtection
{
    private const string Prefix = "enc:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("cosmic-digest-feed-validator-v1");

    public static string? Protect(string? plaintext, string? protectionKey)
    {
        if (string.IsNullOrEmpty(plaintext))
            return plaintext;
        if (string.IsNullOrWhiteSpace(protectionKey))
            return null;

        var key = DeriveKey(protectionKey);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, AssociatedData);
            return Prefix
                + Convert.ToBase64String(nonce) + ":"
                + Convert.ToBase64String(ciphertext) + ":"
                + Convert.ToBase64String(tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    public static string? Unprotect(string? protectedValue, string? protectionKey)
    {
        return TryUnprotect(protectedValue, protectionKey, out var plaintext)
            ? plaintext
            : null;
    }

    public static bool IsProtected(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.StartsWith(Prefix, StringComparison.Ordinal);

    public static bool HasEnvelopeShape(string? value)
    {
        if (!IsProtected(value))
            return false;

        var parts = value![Prefix.Length..].Split(':');
        if (parts.Length != 3)
            return false;

        try
        {
            var nonce = Convert.FromBase64String(parts[0]);
            var ciphertext = Convert.FromBase64String(parts[1]);
            var tag = Convert.FromBase64String(parts[2]);
            return nonce.Length == NonceSize
                && ciphertext.Length > 0
                && tag.Length == TagSize;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool TryUnprotect(
        string? protectedValue,
        string? protectionKey,
        out string? plaintext)
    {
        plaintext = protectedValue;
        if (string.IsNullOrEmpty(protectedValue) || !IsProtected(protectedValue))
            return true;
        if (string.IsNullOrWhiteSpace(protectionKey))
        {
            plaintext = null;
            return false;
        }

        var parts = protectedValue[Prefix.Length..].Split(':');
        if (parts.Length != 3)
        {
            plaintext = null;
            return false;
        }

        var key = DeriveKey(protectionKey);
        try
        {
            var nonce = Convert.FromBase64String(parts[0]);
            var ciphertext = Convert.FromBase64String(parts[1]);
            var tag = Convert.FromBase64String(parts[2]);
            if (nonce.Length != NonceSize || tag.Length != TagSize)
            {
                plaintext = null;
                return false;
            }

            var plaintextBytes = new byte[ciphertext.Length];
            plaintext = null;
            using var aes = new AesGcm(key, TagSize);
            try
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintextBytes, AssociatedData);
                plaintext = Encoding.UTF8.GetString(plaintextBytes);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintextBytes);
            }
        }
        catch (FormatException)
        {
            plaintext = null;
            return false;
        }
        catch (CryptographicException)
        {
            plaintext = null;
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
