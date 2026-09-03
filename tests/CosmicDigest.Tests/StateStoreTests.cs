public sealed class StateStoreTests
{
    [Fact]
    public void MarkReviewed_records_included_and_filtered_candidates()
    {
        var now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var included = new NewsItem("Included", "https://example.com/a?utm_source=rss", now, "Example");
        var filtered = new NewsItem("Filtered", "https://example.com/b", now, "Example");
        var state = new StateOfWorld();

        var includedCandidate = new ScoredArticle(included, 5, new[] { "Backend" }, "event-a");
        var filteredCandidate = new ScoredArticle(filtered, 4, new[] { "Backend" }, "event-b");

        StateStore.MarkReviewed(state, new[] { includedCandidate, filteredCandidate }, new[] { includedCandidate }, now);

        Assert.Equal(2, state.ReviewedArticles.Count);
        Assert.True(state.ReviewedArticles.Single(item => item.Link.EndsWith("/a")).Included);
        Assert.False(state.ReviewedArticles.Single(item => item.Link.EndsWith("/b")).Included);
        Assert.True(state.ReviewedEvents.Single(item => item.EventKey == "event-a").Included);
        Assert.False(state.ReviewedEvents.Single(item => item.EventKey == "event-b").Included);
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
            },
            ReviewedEvents = new List<ReviewedEvent>
            {
                new("expired", now.AddDays(-46), true, "Expired"),
                new("current", now.AddDays(-44), true, "Current")
            }
        };

        StateStore.PruneReviewed(state, now);

        var remaining = Assert.Single(state.ReviewedArticles);
        Assert.Equal("https://example.com/current", remaining.Link);
        Assert.Equal("current", Assert.Single(state.ReviewedEvents).EventKey);
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

    [Fact]
    public void Feed_health_resets_on_success_and_accumulates_real_failures()
    {
        var now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var source = new BriefingSource { Name = "Example", Url = "https://example.com/feed" };
        var state = new StateOfWorld();

        StateStore.UpdateFeedHealth(state, new[]
        {
            new FeedFetchResult(source, "failed", Array.Empty<NewsItem>(), Error: "timeout")
        }, now);
        StateStore.UpdateFeedHealth(state, new[]
        {
            new FeedFetchResult(source, "failed", Array.Empty<NewsItem>(), Error: "timeout")
        }, now.AddMinutes(1));

        Assert.Equal(2, Assert.Single(state.FeedHealth).ConsecutiveFailures);

        StateStore.UpdateFeedHealth(state, new[]
        {
            new FeedFetchResult(source, "not_modified", Array.Empty<NewsItem>(), ETag: "\"v1\"")
        }, now.AddMinutes(2));

        var health = Assert.Single(state.FeedHealth);
        Assert.Equal(0, health.ConsecutiveFailures);
        Assert.Null(health.LastError);
        Assert.Equal("\"v1\"", health.ETag);
    }

    [Fact]
    public void Failed_pending_delivery_restores_only_its_reviewed_items()
    {
        var now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var state = new StateOfWorld();
        var failed = new ScoredArticle(
            new NewsItem("OpenAI agent release", "https://example.com/failed", now, "Example"),
            5,
            new[] { "AI" },
            "event-failed");
        var delivered = new ScoredArticle(
            new NewsItem("Cloud platform release", "https://example.com/delivered", now, "Example"),
            5,
            new[] { "Cloud" },
            "event-delivered");
        StateStore.MarkReviewed(state, new[] { failed }, new[] { failed }, now, "email-failed");
        StateStore.MarkReviewed(state, new[] { delivered }, new[] { delivered }, now.AddMinutes(1), "email-delivered");

        var changed = StateStore.RestoreEligibilityForFailedDelivery(state, "email-failed");

        Assert.True(changed);
        Assert.DoesNotContain(state.ReviewedEvents, item => item.EventKey == "event-failed");
        Assert.DoesNotContain(state.ReviewedArticles, item => item.Link.EndsWith("/failed"));
        Assert.Contains(state.ReviewedEvents, item => item.EventKey == "event-delivered");
        Assert.Contains(state.ReviewedArticles, item => item.Link.EndsWith("/delivered"));
    }
}
