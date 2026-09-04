using System.Net;
using System.Net.Http.Headers;
using System.Text;

public sealed class RssIngestorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");

    [Fact]
    public void Parse_uses_opaque_source_identity_for_durable_article_metadata()
    {
        const string rss = """
            <?xml version="1.0" encoding="UTF-8" ?>
            <rss version="2.0"><channel><title>Ignored title</title>
              <item><title>Agent release</title><link>https://example.com/release</link><pubDate>Thu, 03 Sep 2026 11:00:00 GMT</pubDate><description>Details</description></item>
            </channel></rss>
            """;
        var source = new BriefingSource
        {
            Name = "Private source name",
            Url = "https://example.com/feed?token=secret-value",
            Official = true
        };

        var item = Assert.Single(RssIngestor.Parse(source, rss, Now));

        Assert.Equal(SourceIdentity.ForUrl(source.Url), item.FeedUrl);
        Assert.Equal(SourceIdentity.PublicLabel(source.Url), item.Source);
        Assert.DoesNotContain("secret-value", item.FeedUrl!, StringComparison.Ordinal);
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
    public async Task Fetch_adopts_replacement_validators_returned_with_not_modified()
    {
        var replacementModified = Now.AddMinutes(-15);
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotModified)
            {
                Content = new ByteArrayContent(Array.Empty<byte>())
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"version-2\"");
            response.Content.Headers.LastModified = replacementModified;
            return response;
        });
        using var http = new HttpClient(handler);
        var source = new BriefingSource { Name = "Example", Url = "https://example.com/feed" };
        var previous = new FeedHealthState
        {
            Url = SourceIdentity.ForUrl(source.Url),
            ETag = "\"version-1\"",
            LastModifiedUtc = Now.AddHours(-1)
        };

        var result = await RssIngestor.FetchAsync(new[] { source }, new[] { previous }, Now, httpClient: http);

        var feed = Assert.Single(result.Feeds);
        Assert.Equal("\"version-2\"", feed.ETag);
        Assert.Equal(replacementModified, feed.LastModifiedUtc);
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

    [Fact]
    public async Task Fetch_enforces_the_byte_limit_for_chunked_content()
    {
        var oversized = new byte[(5 * 1024 * 1024) + 1];
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableStream(new MemoryStream(oversized)))
        });
        using var http = new HttpClient(handler);
        var source = new BriefingSource { Name = "Example", Url = "https://example.com/feed" };

        var result = await RssIngestor.FetchAsync(new[] { source }, null, Now, httpClient: http);

        var feed = Assert.Single(result.Feeds);
        Assert.Equal("failed", feed.Status);
        Assert.Contains("5 MB", feed.Error);
        Assert.Empty(feed.Items);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Fetch_retries_httpclient_timeouts()
    {
        var attempt = 0;
        var handler = new RecordingHandler(_ =>
        {
            attempt++;
            if (attempt < 3)
                throw new TaskCanceledException("request timeout");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    <rss version="2.0"><channel><title>Example</title>
                    <item><title>Agent release</title><link>https://example.com/release</link></item>
                    </channel></rss>
                    """)
            };
        });
        using var http = new HttpClient(handler);
        var source = new BriefingSource { Name = "Example", Url = "https://example.com/feed" };

        var result = await RssIngestor.FetchAsync(new[] { source }, null, Now, httpClient: http);

        Assert.Equal("ok", Assert.Single(result.Feeds).Status);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Fetch_bounds_concurrent_source_requests()
    {
        var handler = new ConcurrencyTrackingHandler();
        using var http = new HttpClient(handler);
        var sources = Enumerable.Range(0, 24)
            .Select(index => new BriefingSource
            {
                Name = $"Source {index}",
                Url = $"https://8.8.8.8/feed-{index}"
            })
            .ToList();

        var result = await RssIngestor.FetchAsync(sources, null, Now, httpClient: http);

        Assert.Equal(24, result.Feeds.Count);
        Assert.All(result.Feeds, feed => Assert.Equal("ok", feed.Status));
        Assert.InRange(handler.MaximumConcurrency, 2, 8);
    }

    [Fact]
    public async Task Fetch_blocks_public_redirects_to_private_networks()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("http://127.0.0.1/private-feed");
            return response;
        });
        using var http = new HttpClient(handler);
        var source = new BriefingSource { Name = "Example", Url = "https://8.8.8.8/feed" };

        var result = await RssIngestor.FetchAsync(new[] { source }, null, Now, httpClient: http);

        var feed = Assert.Single(result.Feeds);
        Assert.Equal("failed", feed.Status);
        Assert.Contains("private network", feed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Fetch_validates_each_redirect_and_does_not_forward_validators_cross_origin()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri == new Uri("https://8.8.8.8/feed"))
            {
                var first = new HttpResponseMessage(HttpStatusCode.Redirect);
                first.Headers.Location = new Uri("https://1.1.1.1/feed");
                return first;
            }

            var second = new HttpResponseMessage(HttpStatusCode.Redirect);
            second.Headers.Location = new Uri("http://10.0.0.1/private-feed");
            return second;
        });
        using var http = new HttpClient(handler);
        var source = new BriefingSource { Name = "Example", Url = "https://8.8.8.8/feed" };
        var previous = new FeedHealthState
        {
            Url = source.Url,
            ETag = "\"private-validator\"",
            LastModifiedUtc = Now.AddHours(-1)
        };

        var result = await RssIngestor.FetchAsync(
            new[] { source },
            new[] { previous },
            Now,
            httpClient: http);

        var feed = Assert.Single(result.Feeds);
        Assert.Equal("failed", feed.Status);
        Assert.Contains("private network", feed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.Requests.Count);
        Assert.NotEmpty(handler.Requests[0].Headers.IfNoneMatch);
        Assert.Empty(handler.Requests[1].Headers.IfNoneMatch);
        Assert.Null(handler.Requests[1].Headers.IfModifiedSince);
    }

    [Fact]
    public async Task Fetch_honors_XML_declared_legacy_encoding_without_HTTP_charset()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var windows1252 = Encoding.GetEncoding(1252);
        const string rss = """
            <?xml version="1.0" encoding="windows-1252"?>
            <rss version="2.0"><channel><title>Example</title>
              <item><title>Café – agent release</title><link>https://example.com/release</link></item>
            </channel></rss>
            """;
        var content = new ByteArrayContent(windows1252.GetBytes(rss));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/rss+xml");
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        });
        using var http = new HttpClient(handler);
        var source = new BriefingSource { Name = "Example", Url = "https://example.com/feed" };

        var result = await RssIngestor.FetchAsync(new[] { source }, null, Now, httpClient: http);

        var item = Assert.Single(Assert.Single(result.Feeds).Items);
        Assert.Equal("Café – agent release", item.Title);
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

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class ConcurrencyTrackingHandler : HttpMessageHandler
    {
        private int _currentConcurrency;
        private int _maximumConcurrency;
        public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _currentConcurrency);
            while (true)
            {
                var maximum = Volatile.Read(ref _maximumConcurrency);
                if (current <= maximum
                    || Interlocked.CompareExchange(ref _maximumConcurrency, current, maximum) == maximum)
                {
                    break;
                }
            }

            try
            {
                await Task.Delay(40, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        <rss version="2.0"><channel><title>Example</title>
                        <item><title>Agent release</title><link>https://example.com/release</link></item>
                        </channel></rss>
                        """)
                };
            }
            finally
            {
                Interlocked.Decrement(ref _currentConcurrency);
            }
        }
    }
}
