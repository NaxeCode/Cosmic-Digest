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
        if (string.IsNullOrWhiteSpace(plaintext))
            return plaintext;
        if (string.IsNullOrWhiteSpace(protectionKey))
            return null;
        if (plaintext.StartsWith(Prefix, StringComparison.Ordinal))
            return plaintext;

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
        if (string.IsNullOrWhiteSpace(protectedValue)
            || !protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return protectedValue;
        }
        if (string.IsNullOrWhiteSpace(protectionKey))
            return null;

        var parts = protectedValue[Prefix.Length..].Split(':');
        if (parts.Length != 3)
            return null;

        var key = DeriveKey(protectionKey);
        byte[]? plaintext = null;
        try
        {
            var nonce = Convert.FromBase64String(parts[0]);
            var ciphertext = Convert.FromBase64String(parts[1]);
            var tag = Convert.FromBase64String(parts[2]);
            if (nonce.Length != NonceSize || tag.Length != TagSize)
                return null;

            plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null)
                CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] DeriveKey(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
