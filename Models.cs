using System.Text.Json.Serialization;

public record NewsItem(
    string Title,
    string Link,
    DateTimeOffset Published,
    string Source,
    string? Summary = null);

public sealed record ScoredArticle(
    NewsItem Article,
    double Score,
    IReadOnlyList<string> MatchedPriorities);

public sealed record ReviewedArticle(string Link, DateTimeOffset ReviewedAtUtc, bool Included);

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
}
