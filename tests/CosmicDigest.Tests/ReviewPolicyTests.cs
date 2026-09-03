public sealed class ReviewPolicyTests
{
    [Fact]
    public void Fallback_marks_only_displayed_candidates_reviewed()
    {
        var now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var candidates = Enumerable.Range(1, 7)
            .Select(index => new ScoredArticle(
                new NewsItem($"Article {index}", $"https://example.com/{index}", now, "Example"),
                10 - index,
                new[] { "Backend" }))
            .ToList();
        var displayed = candidates.Take(5).Select(candidate => candidate.Article).ToList();

        var reviewed = ReviewPolicy.CandidatesToMarkReviewed(candidates, displayed, allCandidatesEvaluated: false);

        Assert.Equal(displayed, reviewed);
        Assert.DoesNotContain(candidates[5].Article, reviewed);
    }

    [Fact]
    public void Successful_ai_review_marks_every_evaluated_candidate()
    {
        var now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var candidates = Enumerable.Range(1, 7)
            .Select(index => new ScoredArticle(
                new NewsItem($"Article {index}", $"https://example.com/{index}", now, "Example"),
                10 - index,
                new[] { "Backend" }))
            .ToList();
        var displayed = candidates.Take(2).Select(candidate => candidate.Article).ToList();

        var reviewed = ReviewPolicy.CandidatesToMarkReviewed(candidates, displayed, allCandidatesEvaluated: true);

        Assert.Equal(7, reviewed.Count);
    }
}
