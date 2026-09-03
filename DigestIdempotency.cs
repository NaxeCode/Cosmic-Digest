using System.Security.Cryptography;
using System.Text;

public static class DigestIdempotency
{
    public static PendingDigestSend Prepare(
        StateOfWorld state,
        IReadOnlyList<ScoredArticle> displayed,
        DateTimeOffset preparedAtUtc)
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
            return existing;

        var eventTitles = displayed
            .SelectMany(item => item.IdentityTitles is { Count: > 0 }
                ? item.IdentityTitles
                : new[] { item.Article.Title })
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pending = new PendingDigestSend
        {
            IdempotencyKey = BuildKey(displayed, state.Deliveries),
            PreparedAtUtc = preparedAtUtc,
            EventKeys = eventKeys,
            EventTitles = eventTitles
        };
        state.PendingDigestSends.Add(pending);
        return pending;
    }

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
}
