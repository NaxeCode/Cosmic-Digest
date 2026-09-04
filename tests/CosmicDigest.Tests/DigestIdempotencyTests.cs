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

    [Fact]
    public void Prepared_outbox_replays_the_exact_encrypted_payload_when_corroboration_changes()
    {
        var now = DateTimeOffset.Parse("2026-09-03T23:59:59Z");
        const string apiKey = "test-delivery-key";
        var original = new ScoredArticle(
            new NewsItem("OpenAI releases Agent SDK", "https://example.com/original", now, "Example"),
            5,
            new[] { "AI" },
            EventKey: "event-original",
            IdentityKeys: new[] { "event-original" },
            IdentityTitles: new[] { "OpenAI releases Agent SDK" });
        var state = new StateOfWorld();
        var originalPayload = new PendingEmailPayload(
            "Stella <stella@example.com>",
            "reader@example.com",
            "Original subject",
            "Original text",
            "<p>Original HTML</p>");
        var prepared = DigestIdempotency.Prepare(
            state,
            new[] { original },
            new[] { original },
            now,
            apiKey,
            originalPayload);
        var corroborated = original with
        {
            IdentityKeys = new[] { "event-original", "event-corroboration" },
            IdentityTitles = new[] { original.Article.Title, "Agent SDK released by OpenAI" }
        };

        var retried = DigestIdempotency.Prepare(
            state,
            new[] { corroborated },
            new[] { corroborated },
            now.AddSeconds(2),
            apiKey,
            originalPayload with
            {
                Subject = "Regenerated subject",
                Text = "Regenerated text",
                Html = "<p>Regenerated HTML</p>"
            });

        Assert.Same(prepared.Outbox, retried.Outbox);
        Assert.Equal(prepared.Outbox.IdempotencyKey, retried.Outbox.IdempotencyKey);
        Assert.Equal(originalPayload, retried.Payload);
        Assert.True(retried.Reused);
        Assert.DoesNotContain("Original", prepared.Outbox.PayloadCiphertext, StringComparison.Ordinal);
        Assert.Single(state.PendingDigestSends);

        DigestIdempotency.Complete(state, prepared.Outbox.IdempotencyKey);
        Assert.Empty(state.PendingDigestSends);
    }

    [Fact]
    public void Prepared_outbox_cannot_be_replayed_with_a_different_encryption_key()
    {
        var now = DateTimeOffset.UtcNow;
        var displayed = new[]
        {
            new ScoredArticle(
                new NewsItem("Release", "https://example.com/release", now, "Example"),
                5,
                new[] { "AI" },
                "event-1")
        };
        var state = new StateOfWorld();
        DigestIdempotency.Prepare(
            state,
            displayed,
            displayed,
            now,
            "first-key",
            new PendingEmailPayload("from", "to", "subject", "text", "html"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            DigestIdempotency.ResumeOldest(state, "second-key"));

        Assert.Contains("cannot be decrypted", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prepared_outbox_preserves_included_and_rejected_review_decisions()
    {
        var now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var included = new ScoredArticle(
            new NewsItem("Included", "https://example.com/included", now, "Example"),
            5,
            new[] { "AI" },
            "event-included");
        var rejected = new ScoredArticle(
            new NewsItem("Rejected", "https://example.com/rejected", now, "Example"),
            4,
            new[] { "AI" },
            "event-rejected");
        var state = new StateOfWorld();

        var prepared = DigestIdempotency.Prepare(
            state,
            new[] { included, rejected },
            new[] { included },
            now,
            "test-key",
            new PendingEmailPayload("from", "to", "subject", "text", "html"));

        Assert.Equal(2, DigestIdempotency.ReviewedCandidates(prepared.Outbox).Count);
        Assert.Equal(
            "Included",
            Assert.Single(DigestIdempotency.ReviewedCandidates(prepared.Outbox, included: true)).Article.Title);
        Assert.Equal(
            "Rejected",
            Assert.Single(DigestIdempotency.ReviewedCandidates(prepared.Outbox, included: false)).Article.Title);
    }

    [Fact]
    public void Prepared_outbox_preserves_the_encrypted_functional_link_and_a_safe_identity()
    {
        var now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        const string link = "https://example.com/read?entry=123&signature=private-capability";
        const string key = "stable-link-key";
        var candidate = new ScoredArticle(
            new NewsItem("Private entry", link, now, "Example"),
            5,
            new[] { "AI" },
            "event-private-entry");
        var state = new StateOfWorld();

        var prepared = DigestIdempotency.Prepare(
            state,
            new[] { candidate },
            new[] { candidate },
            now,
            key,
            new PendingEmailPayload("from", "to", "subject", "text", "html"));

        var pendingItem = Assert.Single(prepared.Outbox.ReviewedItems);
        Assert.Equal(link, pendingItem.Article.Link);
        Assert.Equal("https://example.com/read?entry=123", pendingItem.ArticleIdentity);

        var serialized = StateStore.SerializeForStorage(state, key);
        Assert.DoesNotContain("private-capability", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("entry=123", serialized, StringComparison.Ordinal);
        var restored = StateStore.DeserializeFromStorage(serialized, key);
        Assert.Equal(
            "https://example.com/read?entry=123",
            Assert.Single(Assert.Single(restored.PendingDigestSends).ReviewedItems).ArticleIdentity);
        var replayed = Assert.Single(DigestIdempotency.ReviewedCandidates(
            Assert.Single(restored.PendingDigestSends),
            included: true));
        Assert.Equal(link, replayed.Article.Link);
    }
}
