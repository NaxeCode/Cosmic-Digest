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
        var displayed = candidates.Take(5).ToList();

        var reviewed = ReviewPolicy.CandidatesToMarkReviewed(candidates, displayed, allCandidatesEvaluated: false);

        Assert.Equal(displayed, reviewed);
        Assert.DoesNotContain(candidates[5], reviewed);
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
        var displayed = candidates.Take(2).ToList();

        var reviewed = ReviewPolicy.CandidatesToMarkReviewed(candidates, displayed, allCandidatesEvaluated: true);

        Assert.Equal(7, reviewed.Count);
    }

    [Fact]
    public void Successful_ai_review_does_not_suppress_an_omitted_delivery_retry()
    {
        var now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var displayed = new ScoredArticle(
            new NewsItem("Displayed", "https://example.com/displayed", now, "Example"),
            5,
            new[] { "AI" },
            "event-displayed");
        var queuedRetry = new ScoredArticle(
            new NewsItem("Queued retry", "https://example.com/retry", now.AddDays(-3), "Example"),
            1,
            new[] { "AI" },
            "event-retry");

        var reviewed = ReviewPolicy.CandidatesToMarkReviewed(
            new[] { displayed, queuedRetry },
            new[] { displayed },
            allCandidatesEvaluated: true,
            deliveryRetryEventKeys: new[] { "event-retry" });

        Assert.Equal(displayed, Assert.Single(reviewed));
        Assert.DoesNotContain(queuedRetry, reviewed);
    }
}
