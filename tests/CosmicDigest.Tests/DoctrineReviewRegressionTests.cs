using System.Net;

public sealed class DoctrineReviewRegressionTests
{
    [Fact]
    public void Expired_pending_outbox_is_not_replayed_automatically()
    {
        var preparedAt = DateTimeOffset.UtcNow.AddHours(-25);
        var article = new ScoredArticle(
            new NewsItem("Release", "https://example.com/release", preparedAt, "Example"),
            5,
            new[] { "AI" },
            "event-1");
        var state = new StateOfWorld();
        DigestIdempotency.Prepare(
            state,
            new[] { article },
            new[] { article },
            preparedAt,
            "stable-test-key",
            new PendingEmailPayload("from", "to", "subject", "text", "html"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            DigestIdempotency.ResumeOldest(state, "stable-test-key"));

        Assert.Contains("24-hour idempotency window", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(state.PendingDigestSends);
    }

    [Fact]
    public void Same_version_number_for_different_products_does_not_cluster()
    {
        var now = DateTimeOffset.UtcNow;
        var clusters = EventIdentity.Cluster(
            new[]
            {
                new NewsItem(".NET 9 release available now for developers", "https://example.com/dotnet", now, "A"),
                new NewsItem("Java 9 release available now for developers", "https://example.com/java", now, "B")
            },
            0.56);

        Assert.Equal(2, clusters.Count);
    }

    [Fact]
    public void Equivalent_product_version_spellings_still_cluster()
    {
        var now = DateTimeOffset.UtcNow;
        var clusters = EventIdentity.Cluster(
            new[]
            {
                new NewsItem("OpenAI releases GPT-5 for developers", "https://example.com/a", now, "A"),
                new NewsItem("OpenAI releases GPT 5 for developers", "https://example.com/b", now, "B")
            },
            0.56);

        Assert.Single(clusters);
    }

    [Fact]
    public void Incidental_year_price_and_statistic_do_not_split_the_same_product_event()
    {
        var now = DateTimeOffset.UtcNow;
        var clusters = EventIdentity.Cluster(
            new[]
            {
                new NewsItem("Nvidia launches RTX 5090", "https://example.com/base", now, "A"),
                new NewsItem("Nvidia launches RTX 5090 at CES 2026", "https://example.com/year", now.AddMinutes(-1), "B"),
                new NewsItem("Nvidia launches RTX 5090 at $1,999", "https://example.com/price", now.AddMinutes(-2), "C"),
                new NewsItem("Nvidia launches RTX 5090 with 25% faster rendering", "https://example.com/stat", now.AddMinutes(-3), "D")
            },
            0.56);

        var cluster = Assert.Single(clusters);
        Assert.Equal(4, cluster.Articles.Count);
        Assert.True(EventIdentity.ReviewedVersionCanSuppress(
            "Nvidia launches RTX 5090",
            new[] { "Nvidia launches RTX 5090 at CES 2026" }));
    }

    [Fact]
    public void Prepared_outbox_carries_selection_metrics_across_delivery_handoff()
    {
        var now = DateTimeOffset.UtcNow;
        var article = new ScoredArticle(
            new NewsItem("OpenAI agent release", "https://example.com/release", now, "Example"),
            5,
            new[] { "AI" },
            "event-1");
        var metrics = new RunMetrics
        {
            RunAtUtc = now,
            FeedCount = 12,
            HealthyFeedCount = 11,
            FetchedArticleCount = 37,
            CandidateEventCount = 4,
            SelectedEventCount = 1,
            SuppressedEventCount = 3,
            SelectionMode = "ai",
            Model = "gpt-5.6-terra",
            ReasoningEffort = "medium",
            InputTokens = 1234,
            OutputTokens = 321,
            DurationMilliseconds = 987
        };
        var state = new StateOfWorld();

        var prepared = DigestIdempotency.Prepare(
            state,
            new[] { article },
            new[] { article },
            now,
            "stable-test-key",
            new PendingEmailPayload("from", "to", "subject", "text", "html"),
            metrics);

        var persisted = Assert.IsType<RunMetrics>(prepared.Outbox.PreparedMetrics);
        Assert.Equal(12, persisted.FeedCount);
        Assert.Equal(37, persisted.FetchedArticleCount);
        Assert.Equal("gpt-5.6-terra", persisted.Model);
        Assert.Equal(1234, persisted.InputTokens);
        Assert.Equal(987, persisted.DurationMilliseconds);
    }

    [Fact]
    public async Task Permanent_resend_request_rejection_returns_a_rebuildable_terminal_result()
    {
        using var http = new HttpClient(new StaticResponseHandler(
            HttpStatusCode.BadRequest,
            "{\"message\":\"sender is not verified\"}"));
        using var resend = new ResendEmailClient(http);

        var result = await resend.SendAsync(
            "test-key",
            "bad@example.com",
            "reader@example.com",
            "subject",
            "text",
            "<p>html</p>",
            "cosmic-digest-test");

        Assert.Equal("request_rejected", result.Status);
        Assert.StartsWith("rejected:cosmic-digest-test", result.EmailId, StringComparison.Ordinal);
        Assert.True(ResendDeliveryStatus.IsRetryableFailure(result.Status));
        Assert.Equal(
            "request_rejected",
            await resend.WaitForLatestStatusAsync("test-key", result.EmailId));
    }

    [Fact]
    public async Task Transient_resend_request_rejection_keeps_the_outbox_ambiguous()
    {
        using var http = new HttpClient(new StaticResponseHandler(
            HttpStatusCode.TooManyRequests,
            "{\"message\":\"try later\"}"));
        using var resend = new ResendEmailClient(http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => resend.SendAsync(
            "test-key",
            "from@example.com",
            "reader@example.com",
            "subject",
            "text",
            "<p>html</p>",
            "cosmic-digest-test"));
    }

    [Fact]
    public void Incoming_duplicate_refreshes_feed_metadata_before_scoring()
    {
        var published = DateTimeOffset.UtcNow.AddMinutes(-5);
        var state = new StateOfWorld
        {
            CacheNews = new List<NewsItem>
            {
                new("Release", "https://example.com/release", published, "Example", "old summary")
            }
        };
        var incoming = new NewsItem(
            "Release",
            "https://example.com/release",
            published,
            "Example",
            "fresh summary",
            "https://example.com/feed.xml");

        StateStore.AppendNews(state, new[] { incoming });

        var cached = Assert.Single(state.CacheNews);
        Assert.Equal("https://example.com/feed.xml", cached.FeedUrl);
        Assert.Equal("fresh summary", cached.Summary);
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            });
    }
}
