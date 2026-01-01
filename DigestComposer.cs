// DigestComposer.cs
using System.Text;

public static class DigestComposer
{
    public static string BuildMarkdown(
        List<NewsItem> relevant,
        List<PriceItem> prices,
        string? aiSummary = null,
        string? dailyChallenge = null)
    {
        var sb = new StringBuilder();

        // Format timestamp in user's timezone
        var tzName = Environment.GetEnvironmentVariable("TIMEZONE");
        if (string.IsNullOrWhiteSpace(tzName))
            tzName = "America/New_York";
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzName);
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.UtcNow.DateTime, tz);
        var tzAbbr = tz.IsDaylightSavingTime(localTime) ? tz.DaylightName : tz.StandardName;

        // Friendly format: "Tuesday, December 31, 2025 at 2:19 PM EST"
        var formattedDate = localTime.ToString("dddd, MMMM d, yyyy") +
                           " at " + localTime.ToString("h:mm tt") +
                           " " + GetTimezoneAbbreviation(tzName);

        sb.AppendLine($"# Daily Digest — {formattedDate}");
        sb.AppendLine();

        // AI-generated summary (if enabled)
        if (!string.IsNullOrWhiteSpace(aiSummary))
        {
            sb.AppendLine("## AI Summary");

            // Make citation numbers clickable by replacing [1], [2], etc. with links
            var processedSummary = aiSummary;
            if (relevant.Any())
            {
                for (int i = 1; i <= Math.Min(15, relevant.Count); i++)
                {
                    var article = relevant[i - 1];
                    // Replace [1] with [[1]](url) to make it a clickable markdown link
                    processedSummary = System.Text.RegularExpressions.Regex.Replace(
                        processedSummary,
                        $@"\[{i}\](?!\()",  // Match [1] but not [1](url)
                        $"[[{i}]]({article.Link})"
                    );
                }
            }

            sb.AppendLine(processedSummary);
            sb.AppendLine();
        }

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

        // Daily challenge section
        if (!string.IsNullOrWhiteSpace(dailyChallenge))
        {
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine(dailyChallenge);
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

    private static string GetTimezoneAbbreviation(string tzName)
    {
        // Map common timezones to their abbreviations
        return tzName switch
        {
            "America/New_York" => "EST/EDT",
            "America/Chicago" => "CST/CDT",
            "America/Denver" => "MST/MDT",
            "America/Los_Angeles" => "PST/PDT",
            "America/Phoenix" => "MST",
            "Europe/London" => "GMT/BST",
            "Europe/Paris" => "CET/CEST",
            "Asia/Tokyo" => "JST",
            "Australia/Sydney" => "AEDT/AEST",
            _ => TimeZoneInfo.FindSystemTimeZoneById(tzName).StandardName
        };
    }
}
