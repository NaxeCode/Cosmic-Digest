public static class ReviewPolicy
{
    public static IReadOnlyList<ScoredArticle> CandidatesToMarkReviewed(
        IReadOnlyList<ScoredArticle> candidates,
        IReadOnlyList<ScoredArticle> displayed,
        bool allCandidatesEvaluated) =>
        allCandidatesEvaluated
            ? candidates.ToList()
            : displayed;
}
