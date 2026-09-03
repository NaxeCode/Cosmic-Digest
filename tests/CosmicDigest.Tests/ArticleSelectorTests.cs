public sealed class ArticleSelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rank_keeps_only_new_priority_matches()
    {
        var profile = Profile();
        var articles = new[]
        {
            new NewsItem("OpenAI releases a new agent SDK", "https://openai.com/news/agent-sdk?utm_source=rss", Now.AddHours(-2), "OpenAI", "New orchestration APIs."),
            new NewsItem("OpenAI old announcement", "https://openai.com/news/old", Now.AddDays(-4), "OpenAI", "Old."),
            new NewsItem("Celebrity interview", "https://example.com/celebrity", Now.AddHours(-1), "Example", "No engineering content."),
            new NewsItem("OpenAI already reviewed", "https://openai.com/news/seen?utm_medium=email", Now.AddHours(-1), "OpenAI", "Already covered.")
        };

        var ranked = ArticleSelector.Rank(
            articles,
            profile,
            new[] { "https://openai.com/news/seen" },
            Now);

        var result = Assert.Single(ranked);
        Assert.Equal("OpenAI releases a new agent SDK", result.Article.Title);
        Assert.Contains("AI engineering", result.MatchedPriorities);
    }

    [Fact]
    public void CanonicalizeLink_removes_tracking_without_removing_meaningful_query()
    {
        var canonical = ArticleSelector.CanonicalizeLink(
            "https://EXAMPLE.com/path/?id=42&utm_source=rss&fbclid=abc#section");

        Assert.Equal("https://example.com/path?id=42", canonical);
    }

    [Fact]
    public void Rank_deduplicates_tracking_variants()
    {
        var articles = new[]
        {
            new NewsItem("OpenAI agent release", "https://openai.com/news/agents?utm_source=a", Now, "OpenAI"),
            new NewsItem("OpenAI agent release", "https://openai.com/news/agents?utm_source=b", Now.AddMinutes(-1), "OpenAI")
        };

        var ranked = ArticleSelector.Rank(articles, Profile(), Array.Empty<string>(), Now);

        Assert.Single(ranked);
    }

    [Fact]
    public void Rank_does_not_match_short_signals_inside_unrelated_words()
    {
        var profile = Profile();
        profile.Priorities[0].Signals = new List<string> { "AI" };
        var articles = new[]
        {
            new NewsItem("Company said results improved", "https://example.com/results", Now, "Example")
        };

        var ranked = ArticleSelector.Rank(articles, profile, Array.Empty<string>(), Now);

        Assert.Empty(ranked);
    }

    [Fact]
    public void Rank_matches_technical_signals_with_punctuation()
    {
        var profile = Profile();
        profile.Priorities[0].Signals = new List<string> { ".NET" };
        var articles = new[]
        {
            new NewsItem("ASP.NET runtime update", "https://example.com/dotnet", Now, "Example")
        };

        var ranked = ArticleSelector.Rank(articles, profile, Array.Empty<string>(), Now);

        Assert.Single(ranked);
    }

    private static BriefingProfile Profile() => new()
    {
        Version = "test",
        Feeds = new List<string> { "https://example.com/feed" },
        LookbackHours = 36,
        MinimumScore = 1.5,
        TrustedDomains = new List<string> { "openai.com" },
        Priorities = new List<BriefingPriority>
        {
            new()
            {
                Name = "AI engineering",
                Weight = 5,
                Signals = new List<string> { "OpenAI", "agent SDK" }
            }
        }
    };
}
