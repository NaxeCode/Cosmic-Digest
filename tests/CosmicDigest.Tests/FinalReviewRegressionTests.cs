using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public sealed class FinalReviewRegressionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-04T12:00:00Z");

    [Fact]
    public void Clustering_keeps_products_separate_across_generic_version_markers()
    {
        var articles = new[]
        {
            new NewsItem(".NET version 9 release available now for developers", "https://example.com/dotnet-9", Now, "Example"),
            new NewsItem("Java version 9 release available now for developers", "https://example.com/java-9", Now.AddMinutes(-1), "Other")
        };

        var clusters = EventIdentity.Cluster(articles, 0.56);

        Assert.Equal(2, clusters.Count);
    }

    [Fact]
    public void Clustering_retains_multiword_product_context_for_same_number()
    {
        var articles = new[]
        {
            new NewsItem("Microsoft releases SQL Server 2022 update for developers", "https://example.com/sql", Now, "Example"),
            new NewsItem("Microsoft releases Windows Server 2022 update for developers", "https://example.com/windows", Now.AddMinutes(-1), "Other")
        };

        var clusters = EventIdentity.Cluster(articles, 0.56);

        Assert.Equal(2, clusters.Count);
    }

    [Fact]
    public void Version_identity_is_stable_across_headline_word_order()
    {
        const string announced = "Microsoft announces SQL Server 2025";
        const string released = "Microsoft SQL Server 2025 is released";
        var clusters = EventIdentity.Cluster(
            new[]
            {
                new NewsItem(announced, "https://example.com/announced", Now, "Example"),
                new NewsItem(released, "https://example.com/released", Now.AddMinutes(-1), "Other")
            },
            0.56);

        Assert.Single(clusters);
        Assert.True(EventIdentity.ReviewedVersionCanSuppress(announced, new[] { released }));
    }

    [Fact]
    public void Durable_state_redacts_article_link_credentials_everywhere()
    {
        const string secret = "subscriber-secret-token";
        var article = new NewsItem(
            "Private article",
            $"https://example.com/story?access_token={secret}",
            Now,
            "Example",
            FeedUrl: "https://example.com/feed?token=feed-secret");
        var scored = new ScoredArticle(article, 5, new[] { "AI" }, "event-private-link");
        var state = new StateOfWorld();

        StateStore.AppendNews(state, new[] { article });
        StateStore.MarkReviewed(state, new[] { scored }, new[] { scored }, Now);
        StateStore.QueueDeliveryRetries(state, new[] { article }, Now);
        StateStore.RecordDelivery(state, new DeliveryAttempt(
            "email-private",
            Now,
            "Digest",
            "accepted",
            Now,
            IncludedItems: new[] { article }));
        DigestIdempotency.Prepare(
            state,
            new[] { scored },
            new[] { scored },
            Now,
            "stable-test-key",
            new PendingEmailPayload("from", "to", "subject", "text", "html"));

        var serialized = JsonSerializer.Serialize(state);
        Assert.DoesNotContain(secret, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.All(state.CacheNews, item => Assert.Equal("https://example.com/story", item.Link));
        Assert.All(state.ReviewedArticles, item => Assert.Equal("https://example.com/story", item.Link));
        Assert.All(state.DeliveryRetries, item => Assert.Equal("https://example.com/story", item.Article.Link));
    }

    [Fact]
    public void Durable_link_redaction_preserves_meaningful_query_identity()
    {
        var first = SourceIdentity.SanitizeArticleLink(
            "https://example.com/story?id=1&access_token=first-secret&utm_source=rss");
        var second = SourceIdentity.SanitizeArticleLink(
            "https://example.com/story?id=2&access_token=second-secret&utm_source=rss");

        Assert.Equal("https://example.com/story?id=1", first);
        Assert.Equal("https://example.com/story?id=2", second);
        Assert.DoesNotContain("secret", first, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", first, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(first, second);
        Assert.Equal(
            "https://example.com/story?id=1",
            SourceIdentity.SanitizeArticleLink(
                "https://example.com/story?id=1&email=owner@example.com&user_id=42&uid=84"));

        var profile = new BriefingProfile
        {
            LookbackHours = 36,
            CandidateLimit = 5,
            MinimumScore = 1.5,
            EventSimilarityThreshold = 0.56,
            Priorities = new List<BriefingPriority>
            {
                new() { Name = "Engineering", Weight = 5, Signals = new List<string> { "OpenAI" } }
            }
        };
        var ranked = ArticleSelector.Rank(
            new[]
            {
                new NewsItem("OpenAI launches Agent SDK", first, Now, "Example"),
                new NewsItem("OpenAI publishes Kubernetes security guide", second, Now.AddMinutes(-1), "Example")
            },
            profile,
            Array.Empty<string>(),
            Now);
        Assert.Equal(2, ranked.Count);
    }

    [Fact]
    public void Clustering_counts_independent_publishers_not_feed_endpoints()
    {
        var cluster = Assert.Single(EventIdentity.Cluster(
            new[]
            {
                new NewsItem(
                    "Vendor launches Agent SDK 2.0",
                    "https://news.vendor.example/releases/sdk-2",
                    Now,
                    "Vendor news feed",
                    FeedUrl: SourceIdentity.ForUrl("https://vendor.example/news.xml")),
                new NewsItem(
                    "Vendor launches Agent SDK 2.0 for developers",
                    "https://engineering.vendor.example/posts/sdk-2",
                    Now.AddMinutes(-1),
                    "Vendor engineering feed",
                    FeedUrl: SourceIdentity.ForUrl("https://vendor.example/engineering.xml"))
            },
            0.56));

        Assert.Single(cluster.Sources);
    }

    [Fact]
    public void Publisher_identity_uses_complete_public_suffix_rules()
    {
        Assert.Equal("vendor.co.in", PublisherIdentity.ForHost("news.vendor.co.in"));
        Assert.Equal("other.co.in", PublisherIdentity.ForHost("press.other.co.in"));
        Assert.Equal("news.vendor.ck", PublisherIdentity.ForHost("news.vendor.ck"));
        Assert.Equal("www.ck", PublisherIdentity.ForHost("subdomain.www.ck"));

        var cluster = Assert.Single(EventIdentity.Cluster(
            new[]
            {
                new NewsItem("Agent release", "https://news.vendor.co.in/release", Now, "Vendor"),
                new NewsItem("Agent release", "https://press.other.co.in/release", Now.AddMinutes(-1), "Other")
            },
            0.56));
        Assert.Equal(2, cluster.Sources.Count);
    }

    [Fact]
    public void Precluster_cap_does_not_let_one_noisy_source_starve_other_sources()
    {
        var noisyUrl = "https://noisy.example/feed";
        var healthyUrl = "https://healthy.example/feed";
        var profile = new BriefingProfile
        {
            Version = "fairness-test",
            LookbackHours = 36,
            CandidateLimit = 5,
            MinimumScore = 1.5,
            EventSimilarityThreshold = 0.56,
            Priorities = new List<BriefingPriority>
            {
                new() { Name = "Engineering", Weight = 5, Signals = new List<string> { "OpenAI", "Kubernetes" } }
            },
            Sources = new List<BriefingSource>
            {
                new() { Name = "Noisy", Url = noisyUrl },
                new() { Name = "Healthy", Url = healthyUrl }
            },
            Feeds = new List<string> { noisyUrl, healthyUrl }
        };
        var noisy = Enumerable.Range(0, 300)
            .Select(index => new NewsItem(
                "OpenAI launches Agent SDK reliability update",
                $"https://noisy.example/story-{index}",
                Now.AddSeconds(-index),
                SourceIdentity.PublicLabel(noisyUrl),
                FeedUrl: SourceIdentity.ForUrl(noisyUrl)))
            .ToList();
        var healthy = new NewsItem(
            "Kubernetes releases operator reliability update",
            "https://healthy.example/operator-update",
            Now.AddMinutes(-2),
            SourceIdentity.PublicLabel(healthyUrl),
            FeedUrl: SourceIdentity.ForUrl(healthyUrl));

        var ranked = ArticleSelector.Rank(
            noisy.Append(healthy),
            profile,
            Array.Empty<string>(),
            Now);

        Assert.Contains(ranked, item => item.Article.Link == healthy.Link);
        Assert.Contains(ranked, item => item.Article.Link.StartsWith("https://noisy.example/story-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fetch_decodes_common_legacy_charset()
    {
        const string rss = """
            <?xml version="1.0" encoding="windows-1252"?>
            <rss version="2.0"><channel><title>Example</title>
              <item><title>Agent release</title><link>https://example.com/release</link></item>
            </channel></rss>
            """;
        var handler = new StaticResponseHandler(() =>
        {
            var content = new ByteArrayContent(Encoding.ASCII.GetBytes(rss));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/rss+xml")
            {
                CharSet = "windows-1252"
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var http = new HttpClient(handler);
        var source = new BriefingSource { Name = "Example", Url = "https://example.com/feed" };

        var result = await RssIngestor.FetchAsync(new[] { source }, null, Now, httpClient: http);

        Assert.Equal("ok", Assert.Single(result.Feeds).Status);
        Assert.Equal("Agent release", Assert.Single(result.Articles).Title);
    }

    [Fact]
    public async Task Fetch_does_not_retry_permanent_http_failures()
    {
        var handler = new CountingResponseHandler(() =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler);
        var source = new BriefingSource { Name = "Example", Url = "https://example.com/feed" };

        var result = await RssIngestor.FetchAsync(new[] { source }, null, Now, httpClient: http);

        Assert.Equal("failed", Assert.Single(result.Feeds).Status);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public void Parse_bounds_fields_resolves_relative_links_and_rejects_non_web_links()
    {
        var longTitle = new string('T', 500);
        var longSummary = new string('S', 5_000);
        var rss = $"""
            <rss version="2.0"><channel><title>Example</title>
              <item><title>{longTitle}</title><link>/releases/1</link><description>{longSummary}</description></item>
              <item><title>Unsafe</title><link>javascript:alert(1)</link></item>
              <item><title>Also unsafe</title><link>ftp://example.com/file</link></item>
            </channel></rss>
            """;
        var source = new BriefingSource { Name = "Example", Url = "https://example.com/feed/index.xml" };

        var item = Assert.Single(RssIngestor.Parse(source, rss, Now));

        Assert.Equal("https://example.com/releases/1", item.Link);
        Assert.Equal(320, item.Title.Length);
        Assert.Equal(4_000, item.Summary!.Length);
    }

    [Fact]
    public void Successful_unconditional_feed_response_clears_obsolete_validators()
    {
        var source = new BriefingSource { Name = "Example", Url = "https://example.com/feed" };
        var oldModified = Now.AddHours(-1);
        var state = new StateOfWorld
        {
            FeedHealth = new List<FeedHealthState>
            {
                new()
                {
                    Name = source.Name,
                    Url = source.Url,
                    ETag = "\"old\"",
                    LastModifiedUtc = oldModified,
                    LastItemCount = 4
                }
            }
        };
        var item = new NewsItem("Fresh", "https://example.com/fresh", Now, "Example");

        StateStore.UpdateFeedHealth(
            state,
            new[] { new FeedFetchResult(source, "ok", new[] { item }) },
            Now);

        var health = Assert.Single(state.FeedHealth);
        Assert.Null(health.ETag);
        Assert.Null(health.LastModifiedUtc);
        Assert.Equal(1, health.LastItemCount);
    }

    [Fact]
    public void Explicitly_disabled_source_is_not_reenabled_by_legacy_feeds()
    {
        const string json = """
            {
              "version": "test-disabled-source",
              "priorities": [
                {
                  "name": "Backend",
                  "signals": [".NET"]
                }
              ],
              "feeds": [
                "https://disabled.example/feed",
                "https://enabled.example/feed"
              ],
              "sources": [
                {
                  "name": "Explicitly disabled",
                  "url": "https://disabled.example/feed",
                  "enabled": false
                }
              ]
            }
            """;
        var previousBase64 = Environment.GetEnvironmentVariable("DIGEST_PROFILE_B64");
        var previousPath = Environment.GetEnvironmentVariable("DIGEST_PROFILE_PATH");

        try
        {
            Environment.SetEnvironmentVariable("DIGEST_PROFILE_PATH", null);
            Environment.SetEnvironmentVariable(
                "DIGEST_PROFILE_B64",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));

            var profile = BriefingProfileLoader.Load();

            var source = Assert.Single(profile.Sources);
            Assert.Equal("https://enabled.example/feed", source.Url);
            Assert.DoesNotContain("https://disabled.example/feed", profile.Feeds);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DIGEST_PROFILE_B64", previousBase64);
            Environment.SetEnvironmentVariable("DIGEST_PROFILE_PATH", previousPath);
        }
    }

    private sealed class StaticResponseHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory());
    }

    private sealed class CountingResponseHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responseFactory());
        }
    }
}
