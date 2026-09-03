using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed record FeedbackTokenPayload(
    string EventKey,
    string Signal,
    long ExpiresUnixSeconds);

public static class FeedbackTokenService
{
    private static readonly HashSet<string> AllowedSignals = new(StringComparer.Ordinal)
    {
        "useful", "noise", "wrong", "acted"
    };

    public static string? CreateUrl(
        string? baseUrl,
        string? signingKey,
        string eventKey,
        string signal,
        DateTimeOffset expiresAtUtc)
    {
        if (!TryBaseUri(baseUrl, out var baseUri)
            || string.IsNullOrWhiteSpace(signingKey)
            || string.IsNullOrWhiteSpace(eventKey)
            || !AllowedSignals.Contains(signal))
        {
            return null;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new FeedbackTokenPayload(
            eventKey,
            signal,
            expiresAtUtc.ToUnixTimeSeconds()));
        var encodedPayload = Base64UrlEncode(payload);
        var signature = Sign(encodedPayload, signingKey);
        var token = $"{encodedPayload}.{Base64UrlEncode(signature)}";
        return new UriBuilder(baseUri) { Query = $"token={Uri.EscapeDataString(token)}" }.Uri.AbsoluteUri;
    }

    public static bool TryValidate(
        string? token,
        string? signingKey,
        DateTimeOffset now,
        out FeedbackTokenPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(signingKey))
            return false;

        var parts = token.Split('.', 2);
        if (parts.Length != 2)
            return false;

        try
        {
            var expected = Sign(parts[0], signingKey);
            var supplied = Base64UrlDecode(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
                return false;

            payload = JsonSerializer.Deserialize<FeedbackTokenPayload>(Base64UrlDecode(parts[0]));
            return payload is not null
                && !string.IsNullOrWhiteSpace(payload.EventKey)
                && AllowedSignals.Contains(payload.Signal)
                && payload.ExpiresUnixSeconds >= now.ToUnixTimeSeconds();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            payload = null;
            return false;
        }
    }

    private static byte[] Sign(string payload, string key) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(payload));

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }

    private static bool TryBaseUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps)
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }
}

public static class ResendWebhookVerifier
{
    public static bool Verify(
        string rawPayload,
        string? webhookSecret,
        string? messageId,
        string? timestamp,
        string? signatures,
        DateTimeOffset now,
        TimeSpan? tolerance = null)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret)
            || string.IsNullOrWhiteSpace(messageId)
            || string.IsNullOrWhiteSpace(timestamp)
            || string.IsNullOrWhiteSpace(signatures)
            || !long.TryParse(timestamp, out var unixTimestamp))
        {
            return false;
        }

        var signedAt = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        if ((now - signedAt).Duration() > (tolerance ?? TimeSpan.FromMinutes(5)))
            return false;

        try
        {
            var secretValue = webhookSecret.StartsWith("whsec_", StringComparison.Ordinal)
                ? webhookSecret[6..]
                : webhookSecret;
            var secret = Convert.FromBase64String(secretValue);
            var signedContent = Encoding.UTF8.GetBytes($"{messageId}.{timestamp}.{rawPayload}");
            var expected = HMACSHA256.HashData(secret, signedContent);

            return signatures
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Split(',', 2))
                .Where(parts => parts.Length == 2 && parts[0] == "v1")
                .Select(parts => TryDecode(parts[1]))
                .Where(value => value is not null)
                .Any(value => CryptographicOperations.FixedTimeEquals(expected, value!));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[]? TryDecode(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
