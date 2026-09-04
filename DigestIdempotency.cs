using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public static class DigestIdempotency
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly TimeSpan AutomaticReplayWindow = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static PreparedDigestSend Prepare(
        StateOfWorld state,
        IReadOnlyList<ScoredArticle> reviewed,
        IReadOnlyList<ScoredArticle> displayed,
        DateTimeOffset preparedAtUtc,
        string encryptionKey,
        PendingEmailPayload payload)
    {
        var eventKeys = displayed
            .SelectMany(item => item.ReviewEventKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();
        var eventKeySet = eventKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = state.PendingDigestSends
            .Where(item => item.EventKeys.Any(eventKeySet.Contains))
            .OrderByDescending(item => item.PreparedAtUtc)
            .FirstOrDefault();
        if (existing is not null)
        {
            return new PreparedDigestSend(
                existing,
                DecryptPayload(existing, encryptionKey),
                true);
        }

        var eventTitles = displayed
            .SelectMany(ResolveIdentityTitles)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pending = new PendingDigestSend
        {
            IdempotencyKey = BuildKey(displayed, state.Deliveries),
            PreparedAtUtc = preparedAtUtc,
            EventKeys = eventKeys,
            EventTitles = eventTitles,
            ReviewedItems = BuildReviewedItems(reviewed, displayed)
        };
        EncryptPayload(pending, encryptionKey, payload);
        state.PendingDigestSends.Add(pending);
        return new PreparedDigestSend(pending, payload, false);
    }

    public static PreparedDigestSend? ResumeOldest(StateOfWorld state, string encryptionKey)
    {
        var pending = state.PendingDigestSends
            .OrderBy(item => item.PreparedAtUtc)
            .FirstOrDefault();
        if (pending is null)
            return null;

        var age = DateTimeOffset.UtcNow - pending.PreparedAtUtc;
        if (age >= AutomaticReplayWindow)
        {
            throw new InvalidOperationException(
                "The oldest pending digest is outside Resend's 24-hour idempotency window. " +
                "Automatic replay is blocked to avoid a duplicate delivery; inspect and explicitly resolve the durable outbox.");
        }

        return new PreparedDigestSend(pending, DecryptPayload(pending, encryptionKey), true);
    }

    public static List<ScoredArticle> ReviewedCandidates(
        PendingDigestSend pending,
        bool? included = null) =>
        pending.ReviewedItems
            .Where(item => included is null || item.Included == included)
            .Select(item =>
            {
                var eventKeys = item.EventKeys
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var titles = item.EventTitles.Count == eventKeys.Count
                    ? item.EventTitles
                    : eventKeys.Select(_ => item.Article.Title).ToList();
                return new ScoredArticle(
                    item.Article,
                    0,
                    Array.Empty<string>(),
                    eventKeys.FirstOrDefault() ?? EventIdentity.KeyFor(item.Article),
                    IdentityKeys: eventKeys,
                    IdentityTitles: titles);
            })
            .ToList();

    public static void Complete(StateOfWorld state, string idempotencyKey) =>
        state.PendingDigestSends.RemoveAll(item =>
            string.Equals(item.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public static string BuildKey(
        IReadOnlyList<ScoredArticle> displayed,
        IEnumerable<DeliveryAttempt>? priorDeliveries = null)
    {
        var eventKeys = displayed
            .SelectMany(item => item.ReviewEventKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal);
        var digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', eventKeys))))[..16]
            .ToLowerInvariant();
        var baseKey = $"cosmic-digest-{digest}";

        var nextRetry = 0;
        foreach (var delivery in priorDeliveries ?? Array.Empty<DeliveryAttempt>())
        {
            if (!ResendDeliveryStatus.IsRetryableFailure(delivery.Status)
                || string.IsNullOrWhiteSpace(delivery.IdempotencyKey))
            {
                continue;
            }

            if (string.Equals(delivery.IdempotencyKey, baseKey, StringComparison.Ordinal))
            {
                nextRetry = Math.Max(nextRetry, 1);
                continue;
            }

            var retryPrefix = baseKey + "-retry-";
            if (delivery.IdempotencyKey.StartsWith(retryPrefix, StringComparison.Ordinal)
                && int.TryParse(delivery.IdempotencyKey[retryPrefix.Length..], out var retryNumber)
                && retryNumber >= 1)
            {
                nextRetry = Math.Max(nextRetry, retryNumber + 1);
            }
        }

        return nextRetry == 0 ? baseKey : $"{baseKey}-retry-{nextRetry}";
    }

    private static List<PendingDigestItem> BuildReviewedItems(
        IReadOnlyList<ScoredArticle> reviewed,
        IReadOnlyList<ScoredArticle> displayed)
    {
        var includedLinks = displayed
            .Select(item => ArticleSelector.CanonicalizeLink(item.Article.Link))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedEvents = displayed
            .SelectMany(item => item.ReviewEventKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return reviewed.Select(item =>
        {
            var eventKeys = item.ReviewEventKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var eventTitles = ResolveIdentityTitles(item).ToList();
            if (eventTitles.Count != eventKeys.Count)
                eventTitles = eventKeys.Select(_ => item.Article.Title).ToList();

            return new PendingDigestItem
            {
                Article = item.Article,
                EventKeys = eventKeys,
                EventTitles = eventTitles,
                Included = includedLinks.Contains(ArticleSelector.CanonicalizeLink(item.Article.Link))
                    || eventKeys.Any(includedEvents.Contains)
            };
        }).ToList();
    }

    private static IEnumerable<string> ResolveIdentityTitles(ScoredArticle item) =>
        item.IdentityTitles is { Count: > 0 }
            ? item.IdentityTitles
            : item.ReviewEventKeys.Select(_ => item.Article.Title);

    private static void EncryptPayload(
        PendingDigestSend pending,
        string encryptionKey,
        PendingEmailPayload payload)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        var key = DeriveKey(encryptionKey);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(
                nonce,
                plaintext,
                ciphertext,
                tag,
                Encoding.UTF8.GetBytes(pending.IdempotencyKey));
            pending.PayloadNonce = Convert.ToBase64String(nonce);
            pending.PayloadCiphertext = Convert.ToBase64String(ciphertext);
            pending.PayloadTag = Convert.ToBase64String(tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static PendingEmailPayload DecryptPayload(PendingDigestSend pending, string encryptionKey)
    {
        if (string.IsNullOrWhiteSpace(pending.PayloadNonce)
            || string.IsNullOrWhiteSpace(pending.PayloadCiphertext)
            || string.IsNullOrWhiteSpace(pending.PayloadTag))
        {
            throw new InvalidOperationException("The pending digest outbox is missing its encrypted payload.");
        }

        var key = DeriveKey(encryptionKey);
        try
        {
            var nonce = Convert.FromBase64String(pending.PayloadNonce);
            var ciphertext = Convert.FromBase64String(pending.PayloadCiphertext);
            var tag = Convert.FromBase64String(pending.PayloadTag);
            var plaintext = new byte[ciphertext.Length];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(
                    nonce,
                    ciphertext,
                    tag,
                    plaintext,
                    Encoding.UTF8.GetBytes(pending.IdempotencyKey));
                return JsonSerializer.Deserialize<PendingEmailPayload>(plaintext, Json)
                    ?? throw new InvalidOperationException("The pending digest payload is invalid.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException)
        {
            throw new InvalidOperationException(
                "The pending digest payload cannot be decrypted with the current delivery key.",
                ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(string encryptionKey)
    {
        if (string.IsNullOrWhiteSpace(encryptionKey))
            throw new ArgumentException(
                "A dedicated encryption key is required for the pending outbox.",
                nameof(encryptionKey));

        return SHA256.HashData(
            Encoding.UTF8.GetBytes("cosmic-digest-outbox-v1\0" + encryptionKey));
    }
}
