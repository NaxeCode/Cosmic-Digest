using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CodeHollow.FeedReader;

public sealed record FeedFetchResult(
    BriefingSource Source,
    string Status,
    IReadOnlyList<NewsItem> Items,
    string? ETag = null,
    DateTimeOffset? LastModifiedUtc = null,
    string? Error = null)
{
    public bool IsHealthy => Status is "ok" or "not_modified";
}

public sealed record IngestionResult(
    IReadOnlyList<NewsItem> Articles,
    IReadOnlyList<FeedFetchResult> Feeds);

public static class RssIngestor
{
    private const int MaximumFeedBytes = 5 * 1024 * 1024;

    public static async Task<IngestionResult> FetchAsync(
        IEnumerable<BriefingSource> sources,
        IEnumerable<FeedHealthState>? previousHealth,
        DateTimeOffset now,
        int circuitFailureThreshold = 3,
        int circuitHours = 6,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        var healthByUrl = (previousHealth ?? Array.Empty<FeedHealthState>())
            .GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var ownsClient = httpClient is null;
        var http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (!http.DefaultRequestHeaders.UserAgent.Any())
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Cosmic-Digest/2.0 (+https://github.com/NaxeCode/Cosmic-Digest)");

        try
        {
            var tasks = sources.Select(source => FetchOneAsync(
                source,
                healthByUrl.GetValueOrDefault(source.Url),
                now,
                circuitFailureThreshold,
                circuitHours,
                http,
                cancellationToken));
            var feedResults = await Task.WhenAll(tasks);
            var articles = feedResults
                .SelectMany(result => result.Items)
                .OrderByDescending(item => item.Published)
                .ToList();
            return new IngestionResult(articles, feedResults);
        }
        finally
        {
            if (ownsClient)
                http.Dispose();
        }
    }

    private static async Task<FeedFetchResult> FetchOneAsync(
        BriefingSource source,
        FeedHealthState? previous,
        DateTimeOffset now,
        int circuitFailureThreshold,
        int circuitHours,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        if (previous is { ConsecutiveFailures: >= 1, LastFailureUtc: not null }
            && previous.ConsecutiveFailures >= circuitFailureThreshold
            && previous.LastFailureUtc > now.AddHours(-circuitHours))
        {
            return new FeedFetchResult(
                source,
                "circuit_open",
                Array.Empty<NewsItem>(),
                previous.ETag,
                previous.LastModifiedUtc,
                $"Paused after {previous.ConsecutiveFailures} consecutive failures.");
        }

        Exception? lastException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/rss+xml"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/atom+xml"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml", 0.9));
                if (!string.IsNullOrWhiteSpace(previous?.ETag)
                    && EntityTagHeaderValue.TryParse(previous.ETag, out var etag))
                {
                    request.Headers.IfNoneMatch.Add(etag);
                }
                if (previous?.LastModifiedUtc is { } lastModified)
                    request.Headers.IfModifiedSince = lastModified;

                using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    return new FeedFetchResult(
                        source,
                        "not_modified",
                        Array.Empty<NewsItem>(),
                        previous?.ETag,
                        previous?.LastModifiedUtc);
                }

                if (IsTransient(response.StatusCode) && attempt < 3)
                {
                    await DelayBeforeRetry(attempt, cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > MaximumFeedBytes)
                    throw new InvalidDataException("Feed exceeded the 5 MB safety limit.");

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
                    throw new InvalidDataException($"Expected RSS or Atom XML but received {mediaType}.");

                var body = await ReadBoundedAsync(response.Content, cancellationToken);
                var items = Parse(source, body, now);
                return new FeedFetchResult(
                    source,
                    "ok",
                    items,
                    response.Headers.ETag?.ToString(),
                    response.Content.Headers.LastModified);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                if (attempt < 3 && IsTransient(ex))
                {
                    await DelayBeforeRetry(attempt, cancellationToken);
                    continue;
                }
                break;
            }
        }

        var message = CompactError(lastException?.Message ?? "Unknown feed failure.");
        Console.Error.WriteLine($"Feed failed ({source.Name}, {source.Url}): {message}");
        return new FeedFetchResult(
            source,
            "failed",
            Array.Empty<NewsItem>(),
            previous?.ETag,
            previous?.LastModifiedUtc,
            message);
    }

    public static IReadOnlyList<NewsItem> Parse(
        BriefingSource source,
        string content,
        DateTimeOffset fallbackPublishedAt)
    {
        var feed = FeedReader.ReadFromString(content);
        var sourceName = string.IsNullOrWhiteSpace(source.Name)
            ? feed.Title ?? new Uri(source.Url).Host
            : source.Name;

        return (feed.Items ?? Enumerable.Empty<FeedItem>())
            .Select(item =>
            {
                var published = item.PublishingDate
                    ?? (DateTimeOffset.TryParse(item.PublishingDateString, out var parsed)
                        ? parsed
                        : fallbackPublishedAt);
                return new NewsItem(
                    item.Title ?? "",
                    item.Link ?? "",
                    published,
                    sourceName,
                    item.Description,
                    source.Url);
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Title) && !string.IsNullOrWhiteSpace(item.Link))
            .ToList();
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout
        || (int)statusCode == 429
        || (int)statusCode >= 500;

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or IOException or TimeoutException or OperationCanceledException;

    private static async Task<string> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        var initialCapacity = (int)Math.Min(
            content.Headers.ContentLength ?? 64 * 1024,
            MaximumFeedBytes);
        using var bounded = new MemoryStream(capacity: initialCapacity);
        var buffer = new byte[81920];
        while (true)
        {
            var remaining = MaximumFeedBytes - checked((int)bounded.Length);
            var read = await input.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, remaining + 1)),
                cancellationToken);
            if (read == 0)
                break;
            if (read > remaining)
                throw new InvalidDataException("Feed exceeded the 5 MB safety limit.");
            await bounded.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        bounded.Position = 0;
        var charset = content.Headers.ContentType?.CharSet?.Trim('"');
        var encoding = string.IsNullOrWhiteSpace(charset)
            ? Encoding.UTF8
            : Encoding.GetEncoding(charset);
        using var reader = new StreamReader(
            bounded,
            encoding,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static Task DelayBeforeRetry(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(attempt == 1 ? 350 : 900), cancellationToken);

    private static string CompactError(string value)
    {
        var compact = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 300 ? compact : compact[..300] + "…";
    }
}
