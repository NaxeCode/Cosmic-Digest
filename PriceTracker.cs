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
    public async Task<decimal?> FetchAsync(string url)
    {
        // VERY naive: fetch page and try to find a $123.45 pattern.
        // Replace with an API or site-specific parser for reliability.
        try
        {
            var config = Configuration.Default.WithDefaultLoader();
            using var ctx = BrowsingContext.New(config);
            var doc = await ctx.OpenAsync(url);
            var text = doc.DocumentElement?.TextContent ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(text, @"\$\s*([0-9]+(?:\.[0-9]{2})?)");
            if (m.Success && decimal.TryParse(m.Groups[1].Value, out var price)) return price;
        }
        catch { /* ignore */ }
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
