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
    IReadOnlyList<string>? Sources = null)
{
    public IReadOnlyList<string> EvidenceSources =>
        Sources is { Count: > 0 } ? Sources : new[] { Article.Source };
}

public sealed record ReviewedArticle(string Link, DateTimeOffset ReviewedAtUtc, bool Included);

public sealed record ReviewedEvent(
    string EventKey,
    DateTimeOffset ReviewedAtUtc,
    bool Included,
    string Title);

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
    DateTimeOffset StatusAtUtc);

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
    public DateTimeOffset? LastRunUtc { get; set; }
    public DateTimeOffset? LastDigestUtc { get; set; }
    public DateTimeOffset? LegacyMigrationNotBeforeUtc { get; set; }
    public List<NewsItem> CacheNews { get; set; } = new();
    public List<ReviewedArticle> ReviewedArticles { get; set; } = new();
    public List<ReviewedEvent> ReviewedEvents { get; set; } = new();
    public List<FeedHealthState> FeedHealth { get; set; } = new();
    public List<DeliveryAttempt> Deliveries { get; set; } = new();
    public List<RunMetrics> RecentRuns { get; set; } = new();
}
