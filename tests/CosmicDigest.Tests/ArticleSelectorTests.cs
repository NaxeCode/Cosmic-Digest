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

    [Fact]
    public void Rank_clusters_corroborating_sources_into_one_event()
    {
        var articles = new[]
        {
            new NewsItem("OpenAI releases Agent SDK 2.0 for developers", "https://openai.com/agent-sdk-2", Now, "OpenAI"),
            new NewsItem("Agent SDK 2.0 released by OpenAI", "https://example.com/openai-agent-sdk", Now.AddMinutes(-4), "Example")
        };

        var result = Assert.Single(ArticleSelector.Rank(articles, Profile(), Array.Empty<string>(), Now));

        Assert.Equal(2, result.SourceCount);
        Assert.Equal(new[] { "Example", "OpenAI" }, result.EvidenceSources);
        Assert.False(string.IsNullOrWhiteSpace(result.EventKey));
    }

    [Fact]
    public void Rank_suppresses_previously_reviewed_events_even_with_a_new_link()
    {
        var first = Assert.Single(ArticleSelector.Rank(
            new[] { new NewsItem("OpenAI agent SDK release", "https://openai.com/release", Now, "OpenAI") },
            Profile(),
            Array.Empty<string>(),
            Now));

        var replay = ArticleSelector.Rank(
            new[] { new NewsItem("OpenAI agent SDK release", "https://example.com/different-link", Now, "Example") },
            Profile(),
            Array.Empty<string>(),
            Now,
            previouslyReviewedEventKeys: new[] { first.EventKey });

        Assert.Empty(replay);
    }

    [Fact]
    public void Rank_suppresses_remaining_cluster_members_after_the_representative_link_is_removed()
    {
        var articles = new[]
        {
            new NewsItem("OpenAI releases Agent SDK 2.0 for developers", "https://openai.com/agent-sdk-2", Now, "OpenAI"),
            new NewsItem("Agent SDK 2.0 released by OpenAI", "https://example.com/openai-agent-sdk", Now.AddMinutes(-4), "Example")
        };
        var first = Assert.Single(ArticleSelector.Rank(
            articles,
            Profile(),
            Array.Empty<string>(),
            Now));
        var state = new StateOfWorld();
        StateStore.MarkReviewed(state, new[] { first }, new[] { first }, Now);

        var replay = ArticleSelector.Rank(
            articles.Where(item => item.Source == "Example"),
            Profile(),
            state.ReviewedArticles.Select(item => item.Link),
            Now.AddMinutes(1),
            previouslyReviewedEventKeys: state.ReviewedEvents.Select(item => item.EventKey));

        Assert.Empty(replay);
        Assert.Equal(2, state.ReviewedEvents.Count);
        Assert.Contains(state.ReviewedEvents, item => item.Title == articles[0].Title);
        Assert.Contains(state.ReviewedEvents, item => item.Title == articles[1].Title);
    }

    [Fact]
    public void Rank_suppresses_a_corroborating_retitle_that_arrives_on_a_later_run()
    {
        var original = Assert.Single(ArticleSelector.Rank(
            new[]
            {
                new NewsItem("OpenAI releases Agent SDK 2.0 for developers", "https://openai.com/agent-sdk-2", Now, "OpenAI")
            },
            Profile(),
            Array.Empty<string>(),
            Now));
        var state = new StateOfWorld();
        StateStore.MarkReviewed(state, new[] { original }, new[] { original }, Now);

        var replay = ArticleSelector.Rank(
            new[]
            {
                new NewsItem("Agent SDK 2.0 released by OpenAI", "https://example.com/late-report", Now.AddMinutes(1), "Example")
            },
            Profile(),
            state.ReviewedArticles.Select(item => item.Link),
            Now.AddMinutes(2),
            previouslyReviewedEventKeys: state.ReviewedEvents.Select(item => item.EventKey),
            previouslyReviewedEventTitles: state.ReviewedEvents.Select(item => item.Title));

        Assert.Empty(replay);
    }

    [Fact]
    public void Rank_keeps_conflicting_versioned_releases_in_separate_events()
    {
        var articles = new[]
        {
            new NewsItem("OpenAI releases Agent SDK 2.0 for developers", "https://openai.com/agent-sdk-2", Now, "OpenAI"),
            new NewsItem("OpenAI releases Agent SDK 3.0 for developers", "https://openai.com/agent-sdk-3", Now.AddMinutes(-1), "OpenAI")
        };

        var ranked = ArticleSelector.Rank(articles, Profile(), Array.Empty<string>(), Now);

        Assert.Equal(2, ranked.Count);
        Assert.All(ranked, result => Assert.Equal(1, result.SourceCount));
        Assert.NotEqual(ranked[0].EventKey, ranked[1].EventKey);
    }

    [Fact]
    public void Rank_keeps_single_digit_major_versions_in_separate_events()
    {
        var profile = Profile();
        profile.Priorities[0].Signals = new List<string> { ".NET", "release" };
        var articles = new[]
        {
            new NewsItem(".NET 8 release for developers", "https://example.com/dotnet-8", Now, "Example"),
            new NewsItem(".NET 9 release for developers", "https://example.com/dotnet-9", Now.AddMinutes(-1), "Example")
        };

        var ranked = ArticleSelector.Rank(articles, profile, Array.Empty<string>(), Now);

        Assert.Equal(2, ranked.Count);
        Assert.NotEqual(ranked[0].EventKey, ranked[1].EventKey);
    }

    [Fact]
    public void Clustering_does_not_bridge_conflicting_versions_through_an_unversioned_title()
    {
        var articles = new[]
        {
            new NewsItem("OpenAI releases Agent SDK 2.0", "https://example.com/sdk-2", Now, "Example"),
            new NewsItem("OpenAI releases Agent SDK", "https://example.com/sdk", Now.AddMinutes(-1), "Example"),
            new NewsItem("OpenAI releases Agent SDK 3.0", "https://example.com/sdk-3", Now.AddMinutes(-2), "Example")
        };

        var clusters = EventIdentity.Cluster(articles, Profile().EventSimilarityThreshold);

        Assert.Equal(2, clusters.Count);
        Assert.Equal(new[] { 1, 2 }, clusters.Select(cluster => cluster.Articles.Count).Order().ToArray());
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
