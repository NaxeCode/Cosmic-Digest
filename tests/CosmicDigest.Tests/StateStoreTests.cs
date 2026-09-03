public sealed class StateStoreTests
{
    [Fact]
    public void MarkReviewed_records_included_and_filtered_candidates()
    {
        var now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var included = new NewsItem("Included", "https://example.com/a?utm_source=rss", now, "Example");
        var filtered = new NewsItem("Filtered", "https://example.com/b", now, "Example");
        var state = new StateOfWorld();

        StateStore.MarkReviewed(state, new[] { included, filtered }, new[] { included }, now);

        Assert.Equal(2, state.ReviewedArticles.Count);
        Assert.True(state.ReviewedArticles.Single(item => item.Link.EndsWith("/a")).Included);
        Assert.False(state.ReviewedArticles.Single(item => item.Link.EndsWith("/b")).Included);
    }

    [Fact]
    public void PruneReviewed_removes_expired_links_before_selection()
    {
        var now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var state = new StateOfWorld
        {
            ReviewedArticles = new List<ReviewedArticle>
            {
                new("https://example.com/expired", now.AddDays(-46), true),
                new("https://example.com/current", now.AddDays(-44), true)
            }
        };

        StateStore.PruneReviewed(state, now);

        var remaining = Assert.Single(state.ReviewedArticles);
        Assert.Equal("https://example.com/current", remaining.Link);
    }

    [Fact]
    public void Migration_cutoff_persists_until_it_falls_outside_the_lookback()
    {
        var lastDigest = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var state = new StateOfWorld { LastDigestUtc = lastDigest };

        var firstCutoff = StateStore.ResolveCandidateCutoff(state, lastDigest.AddHours(1), 36);
        state.ReviewedArticles.Add(new ReviewedArticle("https://example.com/new", lastDigest.AddHours(1), true));
        var nextCutoff = StateStore.ResolveCandidateCutoff(state, lastDigest.AddHours(24), 36);
        var expiredCutoff = StateStore.ResolveCandidateCutoff(state, lastDigest.AddHours(40), 36);

        Assert.Equal(lastDigest.AddHours(-3), firstCutoff);
        Assert.Equal(lastDigest.AddHours(-3), nextCutoff);
        Assert.Equal(lastDigest.AddHours(4), expiredCutoff);
        Assert.Null(state.LegacyMigrationNotBeforeUtc);
    }
}
