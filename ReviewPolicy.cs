public static class ReviewPolicy
{
    public static IReadOnlyList<NewsItem> CandidatesToMarkReviewed(
        IReadOnlyList<ScoredArticle> candidates,
        IReadOnlyList<NewsItem> displayed,
        bool allCandidatesEvaluated) =>
        allCandidatesEvaluated
            ? candidates.Select(candidate => candidate.Article).ToList()
            : displayed;
}
