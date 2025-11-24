// Models.cs
public record NewsItem(
    string Title,
    string Link,
    DateTimeOffset Published,
    string Source,
    string? Summary = null);

public record TopicTrend(
    string Topic,
    int CountNow,
    int CountPrev,
    double Slope,        // simple linear slope over N days
    double Relevance);   // 0..1

public record PricePoint(DateTimeOffset Ts, decimal Price);

public class PriceItem
{
    public string Name { get; set; }
    public string Url { get; set; }
    public string Currency { get; set; }
    public List<PricePoint> Series { get; set; }

    public PriceItem(string name, string url, string currency, List<PricePoint> series)
    {
        Name = name;
        Url = url;
        Currency = currency;
        Series = series;
    }
}

public class StateOfWorld
{
    public DateTimeOffset? LastDigestUtc { get; set; }
    public List<NewsItem> CacheNews { get; set; } = new();   // last ~7 days (trim)
    public List<PriceItem> Prices { get; set; } = new();
}
