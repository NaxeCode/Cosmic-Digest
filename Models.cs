using System.Text.Json.Serialization;

public sealed record NewsItem(
    string Title,
    string Link,
    DateTimeOffset Published,
    string Source,
    string? Summary = null,
    string? FeedUrl = null);

public sealed record ScoredArticle(
    NewsItem Article,
    double Score,
    IReadOnlyList<string> MatchedPriorities,
    string EventKey = "",
    int SourceCount = 1,
    IReadOnlyList<string>? Sources = null,
    IReadOnlyList<string>? IdentityKeys = null,
    IReadOnlyList<string>? IdentityTitles = null)
{
    public IReadOnlyList<string> EvidenceSources =>
        Sources is { Count: > 0 } ? Sources : new[] { Article.Source };

    public IReadOnlyList<string> ReviewEventKeys =>
        IdentityKeys is { Count: > 0 }
            ? IdentityKeys
            : new[]
            {
                string.IsNullOrWhiteSpace(EventKey)
                    ? EventIdentity.KeyFor(Article)
                    : EventKey
            };
}

public sealed record ReviewedArticle(
    string Link,
    DateTimeOffset ReviewedAtUtc,
    bool Included,
    string? DeliveryEmailId = null);

public sealed record ReviewedEvent(
    string EventKey,
    DateTimeOffset ReviewedAtUtc,
    bool Included,
    string Title,
    string? DeliveryEmailId = null);

public sealed class FeedHealthState
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public DateTimeOffset? LastSuccessUtc { get; set; }
    public DateTimeOffset? LastFailureUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int LastItemCount { get; set; }
    public string? ETag { get; set; }
    public DateTimeOffset? LastModifiedUtc { get; set; }
    public string? LastError { get; set; }
}

public sealed record DeliveryAttempt(
    string EmailId,
    DateTimeOffset SentAtUtc,
    string Subject,
    string Status,
    DateTimeOffset StatusAtUtc,
    string? IdempotencyKey = null,
    IReadOnlyList<NewsItem>? IncludedItems = null);

public sealed record PendingEmailPayload(
    string Sender,
    string Recipient,
    string Subject,
    string Text,
    string Html);

public sealed record PreparedDigestSend(
    PendingDigestSend Outbox,
    PendingEmailPayload Payload,
    bool Reused);

public sealed class PendingDigestItem
{
    public NewsItem Article { get; set; } = new("", "", DateTimeOffset.MinValue, "");
    public List<string> EventKeys { get; set; } = new();
    public List<string> EventTitles { get; set; } = new();
    public bool Included { get; set; }
}

public sealed class PendingDigestSend
{
    public string IdempotencyKey { get; set; } = "";
    public DateTimeOffset PreparedAtUtc { get; set; }
    public List<string> EventKeys { get; set; } = new();
    public List<string> EventTitles { get; set; } = new();
    public string PayloadNonce { get; set; } = "";
    public string PayloadCiphertext { get; set; } = "";
    public string PayloadTag { get; set; } = "";
    public List<PendingDigestItem> ReviewedItems { get; set; } = new();
    public RunMetrics? PreparedMetrics { get; set; }
}

public sealed record DeliveryRetryItem(NewsItem Article, DateTimeOffset QueuedAtUtc);

public sealed class RunMetrics
{
    public DateTimeOffset RunAtUtc { get; set; }
    public int FeedCount { get; set; }
    public int HealthyFeedCount { get; set; }
    public int FetchedArticleCount { get; set; }
    public int CandidateEventCount { get; set; }
    public int SelectedEventCount { get; set; }
    public int SuppressedEventCount { get; set; }
    public string SelectionMode { get; set; } = "deterministic";
    public string? Model { get; set; }
    public string? ReasoningEffort { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public long DurationMilliseconds { get; set; }
}

public sealed class BriefingItem
{
    [JsonPropertyName("article_index")]
    public int ArticleIndex { get; set; }

    [JsonPropertyName("what_changed")]
    public string WhatChanged { get; set; } = "";

    [JsonPropertyName("why_it_matters")]
    public string WhyItMatters { get; set; } = "";

    [JsonPropertyName("decision")]
    public string Decision { get; set; } = "watch";

    [JsonPropertyName("next_step")]
    public string NextStep { get; set; } = "";

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "medium";
}

public sealed class BriefingDocument
{
    [JsonPropertyName("bottom_line")]
    public string BottomLine { get; set; } = "";

    [JsonPropertyName("items")]
    public List<BriefingItem> Items { get; set; } = new();
}

public sealed class StateOfWorld
{
    public int ProtectionVersion { get; set; }
    public DateTimeOffset? LastRunUtc { get; set; }
    public DateTimeOffset? LastDigestUtc { get; set; }
    public DateTimeOffset? LegacyMigrationNotBeforeUtc { get; set; }
    public List<NewsItem> CacheNews { get; set; } = new();
    public List<ReviewedArticle> ReviewedArticles { get; set; } = new();
    public List<ReviewedEvent> ReviewedEvents { get; set; } = new();
    public List<FeedHealthState> FeedHealth { get; set; } = new();
    public List<DeliveryAttempt> Deliveries { get; set; } = new();
    public List<PendingDigestSend> PendingDigestSends { get; set; } = new();
    public List<DeliveryRetryItem> DeliveryRetries { get; set; } = new();
    public List<RunMetrics> RecentRuns { get; set; } = new();
}
