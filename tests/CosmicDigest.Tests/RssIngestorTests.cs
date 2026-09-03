using System.Net;
using System.Net.Http.Headers;

public sealed class RssIngestorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");

    [Fact]
    public void Parse_preserves_configured_source_identity()
    {
        const string rss = """
            <?xml version="1.0" encoding="UTF-8" ?>
            <rss version="2.0"><channel><title>Ignored title</title>
              <item><title>Agent release</title><link>https://example.com/release</link><pubDate>Thu, 03 Sep 2026 11:00:00 GMT</pubDate><description>Details</description></item>
            </channel></rss>
            """;
        var source = new BriefingSource { Name = "Official Example", Url = "https://example.com/feed", Official = true };

        var item = Assert.Single(RssIngestor.Parse(source, rss, Now));

        Assert.Equal("Official Example", item.Source);
        Assert.Equal(source.Url, item.FeedUrl);
        Assert.Equal(DateTimeOffset.Parse("2026-09-03T11:00:00Z"), item.Published);
    }

    [Fact]
    public async Task Fetch_sends_conditional_headers_and_handles_not_modified()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
        using var http = new HttpClient(handler);
        var source = new BriefingSource { Name = "Example", Url = "https://example.com/feed" };
        var previous = new FeedHealthState
        {
            Url = source.Url,
            ETag = "\"version-1\"",
            LastModifiedUtc = Now.AddHours(-1)
        };

        var result = await RssIngestor.FetchAsync(new[] { source }, new[] { previous }, Now, httpClient: http);

        Assert.Equal("not_modified", Assert.Single(result.Feeds).Status);
        Assert.Equal("\"version-1\"", Assert.Single(handler.Requests).Headers.IfNoneMatch.Single().Tag);
        Assert.Equal(previous.LastModifiedUtc, handler.Requests[0].Headers.IfModifiedSince);
    }

    [Fact]
    public async Task Fetch_opens_circuit_after_repeated_recent_failures()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("should not send"));
        using var http = new HttpClient(handler);
        var source = new BriefingSource { Name = "Example", Url = "https://example.com/feed" };
        var previous = new FeedHealthState
        {
            Url = source.Url,
            ConsecutiveFailures = 3,
            LastFailureUtc = Now.AddHours(-1)
        };

        var result = await RssIngestor.FetchAsync(new[] { source }, new[] { previous }, Now, httpClient: http);

        Assert.Equal("circuit_open", Assert.Single(result.Feeds).Status);
        Assert.Empty(handler.Requests);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var copy = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            Requests.Add(copy);
            return Task.FromResult(respond(request));
        }
    }
}
