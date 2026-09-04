using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
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
    private const int MaximumConcurrentFeeds = 8;
    private const int MaximumRedirects = 5;
    private const int XmlDeclarationProbeBytes = 1024;
    private const int MaximumTitleCharacters = 320;
    private const int MaximumSummaryCharacters = 4_000;
    private const int MaximumArticleUrlCharacters = 2_048;
    private static readonly TimeSpan DefaultFeedAttemptTimeout = TimeSpan.FromSeconds(20);

    static RssIngestor()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static async Task<IngestionResult> FetchAsync(
        IEnumerable<BriefingSource> sources,
        IEnumerable<FeedHealthState>? previousHealth,
        DateTimeOffset now,
        int circuitFailureThreshold = 3,
        int circuitHours = 6,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default,
        TimeSpan? feedAttemptTimeout = null)
    {
        var attemptTimeout = feedAttemptTimeout ?? DefaultFeedAttemptTimeout;
        if (attemptTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(feedAttemptTimeout));

        var healthByIdentity = (previousHealth ?? Array.Empty<FeedHealthState>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Url))
            .GroupBy(item => SourceIdentity.NormalizePersisted(item.Url), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (httpClient is not null)
            EnsureUserAgent(httpClient);

        using var gate = new SemaphoreSlim(MaximumConcurrentFeeds, MaximumConcurrentFeeds);
        var tasks = sources.Select(async source =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var previous = healthByIdentity.GetValueOrDefault(SourceIdentity.ForUrl(source.Url));
                if (httpClient is not null)
                {
                    return await FetchOneAsync(
                        source,
                        previous,
                        now,
                        circuitFailureThreshold,
                        circuitHours,
                        httpClient,
                        null,
                        attemptTimeout,
                        cancellationToken);
                }

                var connector = new PinnedAddressConnector();
                using var ownedHttp = CreateOwnedHttpClient(connector);
                return await FetchOneAsync(
                    source,
                    previous,
                    now,
                    circuitFailureThreshold,
                    circuitHours,
                    ownedHttp,
                    connector,
                    attemptTimeout,
                    cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        });
        var feedResults = await Task.WhenAll(tasks);
        var articles = feedResults
            .SelectMany(result => result.Items)
            .OrderByDescending(item => item.Published)
            .ToList();
        return new IngestionResult(articles, feedResults);
    }

    private static async Task<FeedFetchResult> FetchOneAsync(
        BriefingSource source,
        FeedHealthState? previous,
        DateTimeOffset now,
        int circuitFailureThreshold,
        int circuitHours,
        HttpClient http,
        PinnedAddressConnector? connector,
        TimeSpan attemptTimeout,
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
            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCancellation.CancelAfter(attemptTimeout);
            var attemptToken = attemptCancellation.Token;
            try
            {
                var received = await SendWithValidatedRedirectsAsync(
                    source.Url,
                    previous,
                    http,
                    connector,
                    attemptToken);
                using var response = received.Response;
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    return new FeedFetchResult(
                        source,
                        "not_modified",
                        Array.Empty<NewsItem>(),
                        response.Headers.ETag?.ToString() ?? previous?.ETag,
                        response.Content.Headers.LastModified ?? previous?.LastModifiedUtc);
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

                var body = await ReadBoundedAsync(response.Content, attemptToken);
                var items = Parse(source, body, now, received.EffectiveUri.AbsoluteUri);
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

        var message = CompactError(SourceIdentity.RedactFrom(
            lastException?.Message ?? "Unknown feed failure.",
            source.Url));
        Console.Error.WriteLine($"Feed failed ({SourceIdentity.PublicLabel(source.Url)}): {message}");
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
        DateTimeOffset fallbackPublishedAt,
        string? effectiveFeedUrl = null)
    {
        var feed = FeedReader.ReadFromString(content);
        var sourceIdentity = SourceIdentity.ForUrl(source.Url);
        var sourceLabel = SourceIdentity.PublicLabel(sourceIdentity);

        var items = new List<NewsItem>();
        foreach (var item in feed.Items ?? Enumerable.Empty<FeedItem>())
        {
            var title = BoundField(item.Title, MaximumTitleCharacters);
            var link = ResolveArticleLink(effectiveFeedUrl ?? source.Url, item.Link);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                continue;

            var published = item.PublishingDate
                ?? (DateTimeOffset.TryParse(item.PublishingDateString, out var parsed)
                    ? parsed
                    : fallbackPublishedAt);
            items.Add(new NewsItem(
                title,
                link,
                published,
                sourceLabel,
                BoundField(item.Description, MaximumSummaryCharacters),
                sourceIdentity));
        }
        return items;
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout
        || (int)statusCode == 429
        || (int)statusCode >= 500;

    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } statusCode } => IsTransient(statusCode),
        HttpRequestException => true,
        SocketException => true,
        InvalidDataException => false,
        IOException => true,
        TimeoutException => true,
        OperationCanceledException => true,
        _ => false
    };

    private static async Task<(HttpResponseMessage Response, Uri EffectiveUri)> SendWithValidatedRedirectsAsync(
        string sourceUrl,
        FeedHealthState? previous,
        HttpClient http,
        PinnedAddressConnector? connector,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var initialUri)
            || (initialUri.Scheme != Uri.UriSchemeHttps && initialUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidDataException("Feed URL must be absolute HTTP or HTTPS.");
        }

        if (!string.IsNullOrWhiteSpace(initialUri.UserInfo))
            throw new InvalidDataException("Feed URL must not contain embedded credentials.");

        var currentUri = initialUri;
        var initialIsPublic = IsPublicSource(initialUri);
        if (connector is not null && initialIsPublic)
        {
            var initialAddresses = await ResolvePublicAddressesAsync(initialUri, cancellationToken);
            if (initialAddresses is null)
                throw new InvalidDataException("Public feed resolved to a private network destination.");
            connector.Pin(initialUri, initialAddresses);
        }

        for (var redirectCount = 0; ; redirectCount++)
        {
            using var request = CreateFeedRequest(
                currentUri,
                IsSameOrigin(initialUri, currentUri) ? previous : null);
            var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!IsRedirect(response.StatusCode))
                return (response, currentUri);

            if (redirectCount >= MaximumRedirects)
            {
                response.Dispose();
                throw new InvalidDataException("Feed exceeded the redirect safety limit.");
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null
                || !Uri.TryCreate(currentUri, location, out var nextUri)
                || (nextUri.Scheme != Uri.UriSchemeHttps && nextUri.Scheme != Uri.UriSchemeHttp)
                || !string.IsNullOrWhiteSpace(nextUri.UserInfo))
            {
                throw new InvalidDataException("Feed returned an unsafe redirect destination.");
            }

            if (initialIsPublic)
            {
                var nextAddresses = await ResolvePublicAddressesAsync(nextUri, cancellationToken);
                if (nextAddresses is null)
                    throw new InvalidDataException("Public feed redirected to a private network destination.");
                connector?.Pin(nextUri, nextAddresses);
            }
            if (currentUri.Scheme == Uri.UriSchemeHttps
                && nextUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException("Feed redirect attempted to downgrade HTTPS.");
            }
            currentUri = nextUri;
        }
    }

    private static HttpRequestMessage CreateFeedRequest(
        Uri uri,
        FeedHealthState? previous)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
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
        return request;
    }

    private static HttpClient CreateOwnedHttpClient(PinnedAddressConnector connector)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = connector.ConnectAsync
        };
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        EnsureUserAgent(http);
        return http;
    }

    private static void EnsureUserAgent(HttpClient http)
    {
        if (!http.DefaultRequestHeaders.UserAgent.Any())
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Cosmic-Digest/2.0 (+https://github.com/NaxeCode/Cosmic-Digest)");
    }

    private static bool IsSameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase)
        && left.Port == right.Port;

    private static bool IsPublicSource(Uri uri)
    {
        var host = uri.IdnHost.Trim('.');
        if (IsLocalHostName(host))
            return false;
        return !IPAddress.TryParse(host, out var literal) || IsPublicAddress(literal);
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
        || (int)statusCode == 308;

    private static async Task<IPAddress[]?> ResolvePublicAddressesAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        var host = uri.IdnHost.Trim('.');
        if (IsLocalHostName(host))
            return null;

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
            addresses = new[] { literal };
        else
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        return addresses.Length > 0 && addresses.All(IsPublicAddress)
            ? addresses
            : null;
    }

    private static bool IsLocalHostName(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return !address.Equals(IPAddress.IPv6Any)
                && !address.IsIPv6LinkLocal
                && !address.IsIPv6SiteLocal
                && !address.IsIPv6Multicast
                && (bytes[0] & 0xFE) != 0xFC;
        }

        var octets = address.GetAddressBytes();
        return octets[0] != 0
            && octets[0] != 10
            && octets[0] != 127
            && !(octets[0] == 100 && octets[1] is >= 64 and <= 127)
            && !(octets[0] == 169 && octets[1] == 254)
            && !(octets[0] == 172 && octets[1] is >= 16 and <= 31)
            && !(octets[0] == 192 && octets[1] == 168)
            && !(octets[0] == 198 && octets[1] is 18 or 19)
            && octets[0] < 224;
    }

    private static string? ResolveArticleLink(string sourceUrl, string? itemLink)
    {
        if (string.IsNullOrWhiteSpace(itemLink)
            || itemLink.Length > MaximumArticleUrlCharacters)
        {
            return null;
        }

        var trimmed = itemLink.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.RelativeOrAbsolute, out var parsed))
            return null;

        Uri resolved;
        if (parsed.IsAbsoluteUri)
        {
            resolved = parsed;
        }
        else
        {
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri)
                || !Uri.TryCreate(sourceUri, parsed, out var combined))
            {
                return null;
            }
            resolved = combined;
        }

        if ((resolved.Scheme != Uri.UriSchemeHttps && resolved.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrWhiteSpace(resolved.UserInfo)
            || resolved.AbsoluteUri.Length > MaximumArticleUrlCharacters)
        {
            return null;
        }
        return resolved.AbsoluteUri;
    }

    private static string? BoundField(string? value, int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var bounded = value.Length <= maximumCharacters
            ? value
            : value[..maximumCharacters];
        return string.Join(' ', bounded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

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

        var encoding = ResolveEncoding(content, bounded);
        bounded.Position = 0;
        using var reader = new StreamReader(
            bounded,
            encoding,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static Encoding ResolveEncoding(HttpContent content, MemoryStream bounded)
    {
        var charset = content.Headers.ContentType?.CharSet?.Trim('"');
        if (!string.IsNullOrWhiteSpace(charset))
            return Encoding.GetEncoding(charset);

        var probeLength = Math.Min(checked((int)bounded.Length), XmlDeclarationProbeBytes);
        if (probeLength == 0)
            return Encoding.UTF8;

        var probe = Encoding.ASCII.GetString(bounded.GetBuffer(), 0, probeLength);
        var match = Regex.Match(
            probe,
            @"<\?xml\b[^>]*\bencoding\s*=\s*[\""'](?<encoding>[^\""']+)[\""']",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success
            ? Encoding.GetEncoding(match.Groups["encoding"].Value.Trim())
            : Encoding.UTF8;
    }

    private static Task DelayBeforeRetry(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(attempt == 1 ? 350 : 900), cancellationToken);

    private sealed class PinnedAddressConnector
    {
        private const int MaximumPinnedAddresses = 8;
        private static readonly TimeSpan AddressAttemptStagger = TimeSpan.FromMilliseconds(200);
        private readonly ConcurrentDictionary<string, IPAddress[]> _pins =
            new(StringComparer.OrdinalIgnoreCase);

        public void Pin(Uri uri, IEnumerable<IPAddress> addresses) =>
            _pins[Key(uri.IdnHost, uri.Port)] = addresses
                .Distinct()
                .Take(MaximumPinnedAddresses)
                .ToArray();

        public async ValueTask<Stream> ConnectAsync(
            SocketsHttpConnectionContext context,
            CancellationToken cancellationToken)
        {
            var endpoint = context.DnsEndPoint;
            if (!_pins.TryGetValue(Key(endpoint.Host, endpoint.Port), out var addresses))
            {
                addresses = IPAddress.TryParse(endpoint.Host, out var literal)
                    ? new[] { literal }
                    : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);
            }

            addresses = addresses
                .Distinct()
                .Take(MaximumPinnedAddresses)
                .ToArray();
            using var raceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var attempts = addresses
                .Select((address, index) => ConnectAddressAsync(
                    address,
                    endpoint.Port,
                    TimeSpan.FromMilliseconds(AddressAttemptStagger.TotalMilliseconds * index),
                    raceCancellation.Token))
                .ToList();
            Exception? lastException = null;
            while (attempts.Count > 0)
            {
                var completed = await Task.WhenAny(attempts);
                attempts.Remove(completed);
                try
                {
                    var stream = await completed;
                    raceCancellation.Cancel();
                    await DisposeSuccessfulAttemptsAsync(attempts);
                    return stream;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    raceCancellation.Cancel();
                    await DisposeSuccessfulAttemptsAsync(attempts);
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            throw new HttpRequestException(
                $"Unable to connect to feed host '{endpoint.Host}'.",
                lastException);
        }

        private static async Task<Stream> ConnectAddressAsync(
            IPAddress address,
            int port,
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static async Task DisposeSuccessfulAttemptsAsync(
            IEnumerable<Task<Stream>> attempts)
        {
            foreach (var attempt in attempts)
            {
                try
                {
                    (await attempt).Dispose();
                }
                catch
                {
                    // Failed and canceled connection attempts own no live stream.
                }
            }
        }

        private static string Key(string host, int port) => $"{host.Trim('.')}:{port}";
    }

    private static string CompactError(string value)
    {
        var compact = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 300 ? compact : compact[..300] + "…";
    }
}
