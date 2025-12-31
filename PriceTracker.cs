// PriceTracker.cs
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Configuration;

public interface IPriceFetcher
{
    Task<decimal?> FetchAsync(string url);
}

public class NaivePriceFetcher : IPriceFetcher
{
    // EXPERIMENTAL: Simple web scraping for price tracking.
    // Limitations:
    // - Only works on simple product pages with visible prices
    // - May fail on JavaScript-rendered content, paywalls, or anti-scraping measures
    // - Not reliable for production use
    // - For better results, use official APIs (e.g., Amazon Product API, CamelCamelCamel)
    // - Alternative: Leave PRICE_WATCH empty to disable price tracking

    public async Task<decimal?> FetchAsync(string url)
    {
        try
        {
            var config = Configuration.Default.WithDefaultLoader();
            using var ctx = BrowsingContext.New(config);
            var doc = await ctx.OpenAsync(url);
            var text = doc.DocumentElement?.TextContent ?? "";

            // Try multiple currency patterns: $123.45, €123.45, £123.45, 123.45 USD
            var patterns = new[]
            {
                @"[\$€£¥]\s*([0-9]{1,6}(?:[.,][0-9]{2})?)",  // $123.45, €123,45
                @"([0-9]{1,6}(?:[.,][0-9]{2})?)\s*(?:USD|EUR|GBP|JPY)"  // 123.45 USD
            };

            foreach (var pattern in patterns)
            {
                var m = System.Text.RegularExpressions.Regex.Match(text, pattern);
                if (m.Success)
                {
                    var priceStr = m.Groups[1].Value.Replace(',', '.');
                    if (decimal.TryParse(priceStr, out var price) && price > 0 && price < 1000000)
                    {
                        Console.WriteLine($"✓ Price found: {price} from {new Uri(url).Host}");
                        return price;
                    }
                }
            }

            Console.WriteLine($"⚠ No price found at {new Uri(url).Host}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ Price fetch failed for {url}: {ex.Message}");
        }
        return null;
    }
}

public static class PriceTracker
{
    public static async Task<List<PriceItem>> UpdateAsync(StateOfWorld s, IEnumerable<(string Name, string Url, string Cur)> watch, IPriceFetcher fetcher)
    {
        var list = new List<PriceItem>();
        foreach (var w in watch)
        {
            var existing = s.Prices.FirstOrDefault(p => p.Name == w.Name && p.Url == w.Url)
                           ?? new PriceItem(w.Name, w.Url, w.Cur, new List<PricePoint>());
            var price = await fetcher.FetchAsync(w.Url);
            if (price.HasValue)
            {
                existing.Series.Add(new PricePoint(DateTimeOffset.UtcNow, price.Value));
                // keep last 90 days
                var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
                existing.Series = existing.Series.Where(pp => pp.Ts >= cutoff).ToList();
            }
            list.Add(existing);
        }
        return list;
    }

    public static (string Decision, string Rationale) BuyHold(PriceItem item)
    {
        if (item.Series.Count < 3) return ("WATCH", "Insufficient history");

        var last = item.Series.OrderByDescending(p => p.Ts).First().Price;
        var min90 = item.Series.Min(p => p.Price);
        var avg30 = item.Series.Where(p => p.Ts >= DateTimeOffset.UtcNow.AddDays(-30)).DefaultIfEmpty().Average(p => p?.Price ?? last);

        if (last <= min90 * 1.01m) return ("BUY", $"Near 90-day low ({last} vs {min90})");
        if (last <= avg30 * 0.95m) return ("BUY", $"Below 30-day avg ({last} vs {avg30})");
        return ("HOLD", $"Price {last} above recent lows/avg");
    }
}
