public static class ReviewPolicy
{
    public static IReadOnlyList<ScoredArticle> CandidatesToMarkReviewed(
        IReadOnlyList<ScoredArticle> candidates,
        IReadOnlyList<ScoredArticle> displayed,
        bool allCandidatesEvaluated,
        IEnumerable<string>? deliveryRetryEventKeys = null)
    {
        if (!allCandidatesEvaluated)
            return displayed;

        var retryKeys = (deliveryRetryEventKeys ?? Array.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (retryKeys.Count == 0)
            return candidates.ToList();

        var displayedKeys = displayed
            .SelectMany(item => item.ReviewEventKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates
            .Where(candidate =>
                !candidate.ReviewEventKeys.Any(retryKeys.Contains)
                || candidate.ReviewEventKeys.Any(displayedKeys.Contains))
            .ToList();
    }
}
