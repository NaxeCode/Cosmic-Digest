// DigestComposer.cs
using System.Text;

public static class DigestComposer
{
    public static string BuildMarkdown(
        List<TopicTrend> developing,
        List<NewsItem> relevant,
        List<PriceItem> prices,
        string? aiSummary = null)
    {
        var sb = new StringBuilder();
        var now = DateTimeOffset.UtcNow.ToString("u");

        sb.AppendLine($"# 3-Day Digest — " + now);
        sb.AppendLine();

        // AI-generated summary (if enabled)
        if (!string.IsNullOrWhiteSpace(aiSummary))
        {
            sb.AppendLine("## AI Summary");
            sb.AppendLine(aiSummary);
            sb.AppendLine();
        }

        // Developing stories
        sb.AppendLine("## Developing stories (what changed)");
        foreach (var t in developing.Take(6))
            sb.AppendLine($"- **{t.Topic}** — now: {t.CountNow}, prev: {t.CountPrev}, Δ: {t.Slope:+#;-#;0}");

        sb.AppendLine();
        sb.AppendLine("## Worldwide but relevant to you");
        foreach (var n in relevant.Take(8))
            sb.AppendLine($"- [{n.Title}]({n.Link}) — {n.Source} ({n.Published:yyyy-MM-dd})");

        sb.AppendLine();
        sb.AppendLine("## Price trends (watchlist)");
        foreach (var p in prices)
        {
            var (decision, why) = PriceTracker.BuyHold(p);
            var last = p.Series.OrderByDescending(x => x.Ts).FirstOrDefault();
            sb.AppendLine($"- **{p.Name}** — {decision}. {why}. Latest: {(last is null ? "n/a" : $"{last.Price} {p.Currency} on {last.Ts:yyyy-MM-dd}")}");
        }

        // metadata footer
        var footer = new
        {
            version = "v0.2",
            digest_id = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ"),
            items_considered = relevant.Count,
            price_items = prices.Select(p => new {
                p.Name,
                p.Url,
                last = p.Series.OrderByDescending(x => x.Ts).FirstOrDefault()?.Price
            })
        };
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(footer));
        sb.AppendLine("```");

        return sb.ToString();
    }
}
