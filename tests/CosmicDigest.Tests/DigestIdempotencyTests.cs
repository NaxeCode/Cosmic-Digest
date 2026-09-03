public sealed class DigestIdempotencyTests
{
    [Fact]
    public void Key_stays_stable_for_ambiguous_retries_and_advances_after_terminal_failure()
    {
        var now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var displayed = new[]
        {
            new ScoredArticle(
                new NewsItem("Release", "https://example.com/release", now, "Example"),
                5,
                new[] { "AI" },
                "event-1")
        };
        var baseKey = DigestIdempotency.BuildKey(displayed);
        var pending = new DeliveryAttempt(
            "email-pending",
            now,
            "Subject",
            "accepted",
            now,
            baseKey);

        Assert.DoesNotContain("20260903", baseKey);
        Assert.Equal(baseKey, DigestIdempotency.BuildKey(displayed, new[] { pending }));

        var failed = pending with { EmailId = "email-failed", Status = "bounced" };
        var retryOne = DigestIdempotency.BuildKey(displayed, new[] { failed });
        Assert.Equal(baseKey + "-retry-1", retryOne);

        var failedRetry = failed with { EmailId = "email-failed-retry", IdempotencyKey = retryOne };
        Assert.Equal(
            baseKey + "-retry-2",
            DigestIdempotency.BuildKey(displayed, new[] { failed, failedRetry }));
    }
}
