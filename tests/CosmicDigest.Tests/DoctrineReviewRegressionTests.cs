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
