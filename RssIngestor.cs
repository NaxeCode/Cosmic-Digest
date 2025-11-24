// RssIngestor.cs
using CodeHollow.FeedReader;

public static class RssIngestor
{
    public static async Task<List<NewsItem>> FetchAsync(IEnumerable<string> feeds)
    {
        var results = new List<NewsItem>();
        foreach (var url in feeds)
        {
            try
            {
                var feed = await FeedReader.ReadAsync(url);
                var src = feed?.Title ?? new Uri(url).Host;
                foreach (var e in feed?.Items ?? Enumerable.Empty<FeedItem>())
                {
                    var pub = e.PublishingDate ?? (e.PublishingDateString != null
                        ? (DateTimeOffset.TryParse(e.PublishingDateString, out var dt) ? dt : DateTimeOffset.UtcNow)
                        : DateTimeOffset.UtcNow);
                    results.Add(new NewsItem(e.Title ?? "", e.Link ?? "", pub, src, e.Description));
                }
            }
            catch { /* swallow per-feed errors */ }
        }
        return results.OrderByDescending(n => n.Published).ToList();
    }
}
