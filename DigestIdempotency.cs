using System.Security.Cryptography;
using System.Text;

public static class DigestIdempotency
{
    public static string BuildKey(
        DateTimeOffset sentAtUtc,
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
        var baseKey = $"cosmic-digest-{sentAtUtc:yyyyMMdd}-{digest}";

        var nextRetry = 0;
        foreach (var delivery in priorDeliveries ?? Array.Empty<DeliveryAttempt>())
        {
            if (!ResendDeliveryStatus.IsFailure(delivery.Status)
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
