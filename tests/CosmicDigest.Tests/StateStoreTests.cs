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
}
