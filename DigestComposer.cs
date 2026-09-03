using System.Text;
using System.Text.RegularExpressions;

public static class DigestComposer
{
    public static string BuildMarkdown(
        BriefingProfile profile,
        IReadOnlyList<ScoredArticle> candidates,
        BriefingDocument briefing,
        DateTimeOffset generatedAtUtc)
    {
        var sb = new StringBuilder();
        var localTime = ToLocalTime(generatedAtUtc);
        var possessiveName = profile.DisplayName.Equals("Your", StringComparison.OrdinalIgnoreCase)
            ? "Your"
            : $"{profile.DisplayName}'s";

        sb.AppendLine($"# {possessiveName} Intelligence Brief");
        sb.AppendLine();
        sb.AppendLine($"{localTime:dddd, MMMM d, yyyy} · Profile `{EscapeInline(profile.Version)}`");
        sb.AppendLine();
        sb.AppendLine($"> {EscapeText(briefing.BottomLine)}");

        AppendSection(sb, "Act", "act", candidates, briefing.Items);
        AppendSection(sb, "Watch", "watch", candidates, briefing.Items);

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"Evaluated {candidates.Count} new candidate{(candidates.Count == 1 ? "" : "s")}. No quota filling; previously sent links are suppressed.");

        return sb.ToString();
    }

    public static string BuildSubject(BriefingProfile profile, BriefingDocument briefing)
    {
        var actionCount = briefing.Items.Count(item => item.Decision == "act");
        var prefix = profile.DisplayName.Equals("Your", StringComparison.OrdinalIgnoreCase)
            ? "Daily intelligence"
            : $"{profile.DisplayName} intelligence";

        return actionCount > 0
            ? $"{prefix}: {actionCount} action signal{(actionCount == 1 ? "" : "s")}"
            : $"{prefix}: {briefing.Items.Count} material update{(briefing.Items.Count == 1 ? "" : "s")}";
    }

    public static IReadOnlyList<NewsItem> DisplayedArticles(
        IReadOnlyList<ScoredArticle> candidates,
        BriefingDocument briefing) =>
        briefing.Items
            .Where(item => item.Decision is "act" or "watch")
            .Where(item => item.ArticleIndex >= 1 && item.ArticleIndex <= candidates.Count)
            .Select(item => candidates[item.ArticleIndex - 1].Article)
            .DistinctBy(article => ArticleSelector.CanonicalizeLink(article.Link))
            .ToList();

    private static void AppendSection(
        StringBuilder sb,
        string heading,
        string decision,
        IReadOnlyList<ScoredArticle> candidates,
        IEnumerable<BriefingItem> items)
    {
        var selected = items
            .Where(item => item.Decision == decision)
            .Where(item => item.ArticleIndex >= 1 && item.ArticleIndex <= candidates.Count)
            .ToList();
        if (selected.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"## {heading}");

        foreach (var item in selected)
        {
            var article = candidates[item.ArticleIndex - 1].Article;
            sb.AppendLine();
            sb.AppendLine($"### [{EscapeLinkText(article.Title)}]({SafeLink(article.Link)})");
            sb.AppendLine();
            sb.AppendLine($"**What changed:** {EscapeText(item.WhatChanged)}");
            sb.AppendLine();
            sb.AppendLine($"**Why it matters:** {EscapeText(item.WhyItMatters)}");
            if (!string.IsNullOrWhiteSpace(item.NextStep))
            {
                sb.AppendLine();
                sb.AppendLine($"**Next move:** {EscapeText(item.NextStep)}");
            }

            sb.AppendLine();
            sb.AppendLine($"Evidence: {EscapeText(article.Source)} · {article.Published:yyyy-MM-dd} · {item.Confidence} confidence");
        }
    }

    private static DateTimeOffset ToLocalTime(DateTimeOffset utc)
    {
        var configuredTimezone = Environment.GetEnvironmentVariable("TIMEZONE");
        var timezone = string.IsNullOrWhiteSpace(configuredTimezone)
            ? "America/New_York"
            : configuredTimezone;
        try
        {
            return TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(timezone));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            Console.Error.WriteLine($"Unknown TIMEZONE '{timezone}'; using UTC.");
            return utc;
        }
    }

    private static string EscapeInline(string value) => value.Replace("`", "'");

    private static string EscapeLinkText(string value) => EscapeText(value);

    private static string EscapeText(string? value)
    {
        var compact = string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var escapedMarkdown = Regex.Replace(
            compact,
            @"[\\`*_\[\]()]",
            match => "\\" + match.Value);
        return System.Net.WebUtility.HtmlEncode(escapedMarkdown).Trim();
    }

    private static string SafeLink(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri.Replace("(", "%28").Replace(")", "%29")
            : "#";
}
