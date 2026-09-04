using System.Net;
using System.Net.Http.Headers;
using System.Text;

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
}
