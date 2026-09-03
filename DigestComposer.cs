using System.Net;
using System.Text;
using System.Text.RegularExpressions;

public sealed record EmailBrandOptions(
    string BrandName,
    string AvatarUrl,
    string? FeedbackBaseUrl = null,
    string? FeedbackSigningKey = null)
{
    public static EmailBrandOptions FromEnvironment() => new(
        Environment.GetEnvironmentVariable("BRAND_NAME") ?? "Stella · Cosmic Digest",
        Environment.GetEnvironmentVariable("BRAND_AVATAR_URL")
            ?? "https://raw.githubusercontent.com/NaxeCode/Cosmic-Digest/main/assets/brand/stella-avatar-128.png",
        Environment.GetEnvironmentVariable("FEEDBACK_BASE_URL"),
        Environment.GetEnvironmentVariable("FEEDBACK_SIGNING_KEY"));
}

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
        var possessiveName = PossessiveName(profile.DisplayName);

        sb.AppendLine($"# {possessiveName} Intelligence Brief");
        sb.AppendLine();
        sb.AppendLine($"{localTime:dddd, MMMM d, yyyy} · Stella · Cosmic Digest");
        sb.AppendLine();
        sb.AppendLine($"> {EscapeMarkdown(briefing.BottomLine)}");
        sb.AppendLine();
        sb.AppendLine(BuildStatusLine(briefing));

        AppendTextSection(sb, "Act", "act", candidates, briefing.Items);
        AppendTextSection(sb, "Watch", "watch", candidates, briefing.Items);
        AppendTextSection(sb, "Learn", "learn", candidates, briefing.Items);

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine(BuildTransparencyLine(candidates.Count, briefing.Items.Count));

        return sb.ToString();
    }

    public static string BuildHtml(
        BriefingProfile profile,
        IReadOnlyList<ScoredArticle> candidates,
        BriefingDocument briefing,
        DateTimeOffset generatedAtUtc,
        EmailBrandOptions? brand = null)
    {
        brand ??= EmailBrandOptions.FromEnvironment();
        var localTime = ToLocalTime(generatedAtUtc);
        var possessiveName = PossessiveName(profile.DisplayName);
        var preheader = Html(briefing.BottomLine);
        var body = new StringBuilder();

        body.AppendLine("<!doctype html>");
        body.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        body.AppendLine("<meta name=\"color-scheme\" content=\"light dark\"><meta name=\"supported-color-schemes\" content=\"light dark\">");
        body.AppendLine("<title>Intelligence Brief</title>");
        body.AppendLine("<style>@media (prefers-color-scheme:dark){.page{background:#0b1020!important}.shell{background:#12182a!important}.copy{color:#e8ecf7!important}.muted{color:#aab4cf!important}.panel{background:#1a2238!important}.rule{border-color:#2b3653!important}}</style></head>");
        body.AppendLine("<body class=\"page\" style=\"margin:0;padding:0;background:#eef1f7;color:#172033;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;\">");
        body.AppendLine($"<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;\">{preheader}&#847; &#847; &#847;</div>");
        body.AppendLine("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"width:100%;background:#eef1f7;\"><tr><td align=\"center\" style=\"padding:24px 12px;\">");
        body.AppendLine("<table role=\"presentation\" class=\"shell\" width=\"640\" cellspacing=\"0\" cellpadding=\"0\" style=\"width:100%;max-width:640px;background:#ffffff;border-radius:18px;overflow:hidden;\">");
        body.AppendLine("<tr><td style=\"padding:30px 30px 18px;\">");
        body.AppendLine("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\"><tr>");
        body.AppendLine($"<td width=\"58\" valign=\"middle\"><img src=\"{Attribute(SafeUrl(brand.AvatarUrl))}\" width=\"48\" height=\"48\" alt=\"Stella\" style=\"display:block;width:48px;height:48px;border-radius:50%;object-fit:cover;\"></td>");
        body.AppendLine($"<td valign=\"middle\"><div style=\"font-size:12px;line-height:16px;font-weight:800;letter-spacing:1.3px;text-transform:uppercase;color:#6657d9;\">{Html(brand.BrandName)}</div><div class=\"muted\" style=\"margin-top:3px;font-size:13px;line-height:18px;color:#69748c;\">Your personalized signal from the stars</div></td>");
        body.AppendLine("</tr></table></td></tr>");
        body.AppendLine("<tr><td style=\"padding:0 30px 8px;\">");
        body.AppendLine($"<h1 class=\"copy\" style=\"margin:0;color:#11182a;font-size:30px;line-height:37px;letter-spacing:-0.7px;\">{Html(possessiveName)} Intelligence Brief</h1>");
        body.AppendLine($"<p class=\"muted\" style=\"margin:7px 0 0;color:#69748c;font-size:14px;line-height:20px;\">{localTime:dddd, MMMM d, yyyy}</p>");
        body.AppendLine("</td></tr>");
        body.AppendLine("<tr><td style=\"padding:18px 30px 0;\">");
        body.AppendLine($"<div class=\"panel copy\" style=\"padding:18px 20px;background:#f2f1ff;border-left:4px solid #6657d9;border-radius:10px;color:#24214f;font-size:17px;line-height:25px;font-weight:650;\">{Html(briefing.BottomLine)}</div>");
        body.AppendLine("</td></tr>");
        body.AppendLine("<tr><td style=\"padding:14px 30px 10px;\">");
        body.AppendLine($"<div class=\"muted\" style=\"font-size:12px;line-height:18px;font-weight:750;letter-spacing:.45px;color:#69748c;\">{Html(BuildStatusLine(briefing).ToUpperInvariant())}</div>");
        body.AppendLine("</td></tr>");

        AppendHtmlSection(body, "ACT", "act", "#ee624f", candidates, briefing.Items, brand, generatedAtUtc);
        AppendHtmlSection(body, "WATCH", "watch", "#6657d9", candidates, briefing.Items, brand, generatedAtUtc);
        AppendHtmlSection(body, "LEARN", "learn", "#087f8c", candidates, briefing.Items, brand, generatedAtUtc);

        body.AppendLine("<tr><td class=\"rule\" style=\"padding:20px 30px 30px;border-top:1px solid #e4e8f0;\">");
        body.AppendLine($"<p class=\"muted\" style=\"margin:0;color:#7b8499;font-size:12px;line-height:18px;\">{Html(BuildTransparencyLine(candidates.Count, briefing.Items.Count))}</p>");
        body.AppendLine("<p class=\"muted\" style=\"margin:7px 0 0;color:#7b8499;font-size:12px;line-height:18px;\">Stella watches the world so you don’t have to.</p>");
        body.AppendLine("</td></tr></table></td></tr></table></body></html>");
        return body.ToString();
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

    public static IReadOnlyList<ScoredArticle> DisplayedCandidates(
        IReadOnlyList<ScoredArticle> candidates,
        BriefingDocument briefing) =>
        briefing.Items
            .Where(item => item.Decision is "act" or "watch" or "learn")
            .Where(item => item.ArticleIndex >= 1 && item.ArticleIndex <= candidates.Count)
            .Select(item => candidates[item.ArticleIndex - 1])
            .DistinctBy(candidate => string.IsNullOrWhiteSpace(candidate.EventKey)
                ? ArticleSelector.CanonicalizeLink(candidate.Article.Link)
                : candidate.EventKey)
            .ToList();

    public static IReadOnlyList<NewsItem> DisplayedArticles(
        IReadOnlyList<ScoredArticle> candidates,
        BriefingDocument briefing) =>
        DisplayedCandidates(candidates, briefing).Select(candidate => candidate.Article).ToList();

    private static void AppendTextSection(
        StringBuilder sb,
        string heading,
        string decision,
        IReadOnlyList<ScoredArticle> candidates,
        IEnumerable<BriefingItem> items)
    {
        var selected = ValidItems(decision, candidates, items);
        if (selected.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"## {heading}");
        foreach (var (item, candidate) in selected)
        {
            var article = candidate.Article;
            sb.AppendLine();
            sb.AppendLine($"### [{EscapeLinkText(article.Title)}]({SafeUrl(article.Link)})");
            sb.AppendLine();
            sb.AppendLine($"**What changed:** {EscapeMarkdown(item.WhatChanged)}");
            sb.AppendLine();
            sb.AppendLine($"**Why for you:** {EscapeMarkdown(item.WhyItMatters)}");
            if (!string.IsNullOrWhiteSpace(item.NextStep))
            {
                sb.AppendLine();
                sb.AppendLine($"**Next move:** {EscapeMarkdown(item.NextStep)}");
            }

            sb.AppendLine();
            sb.AppendLine($"Evidence: {EscapeMarkdown(string.Join(", ", candidate.EvidenceSources))} · {article.Published:yyyy-MM-dd} · {item.Confidence} confidence{CorroborationSuffix(candidate)}");
        }
    }

    private static void AppendHtmlSection(
        StringBuilder sb,
        string heading,
        string decision,
        string color,
        IReadOnlyList<ScoredArticle> candidates,
        IEnumerable<BriefingItem> items,
        EmailBrandOptions brand,
        DateTimeOffset generatedAtUtc)
    {
        var selected = ValidItems(decision, candidates, items);
        if (selected.Count == 0)
            return;

        sb.AppendLine($"<tr><td style=\"padding:22px 30px 4px;\"><div style=\"font-size:12px;line-height:18px;font-weight:850;letter-spacing:1.4px;color:{color};\">{heading}</div></td></tr>");
        foreach (var (item, candidate) in selected)
        {
            var article = candidate.Article;
            sb.AppendLine("<tr><td class=\"rule\" style=\"padding:15px 30px 24px;border-bottom:1px solid #e4e8f0;\">");
            sb.AppendLine($"<h2 class=\"copy\" style=\"margin:0;color:#172033;font-size:20px;line-height:27px;letter-spacing:-.2px;\"><a href=\"{Attribute(SafeUrl(article.Link))}\" style=\"color:#172033;text-decoration:underline;text-decoration-color:{color};text-underline-offset:3px;\">{Html(article.Title)}</a></h2>");
            AppendLabeledParagraph(sb, "What changed", item.WhatChanged);
            AppendLabeledParagraph(sb, "Why for you", item.WhyItMatters);
            if (!string.IsNullOrWhiteSpace(item.NextStep))
                AppendLabeledParagraph(sb, "Next move", item.NextStep);
            sb.AppendLine($"<p class=\"muted\" style=\"margin:14px 0 0;color:#737e95;font-size:12px;line-height:18px;\">{Html(string.Join(", ", candidate.EvidenceSources))} · {article.Published:yyyy-MM-dd} · {Html(item.Confidence)} confidence{Html(CorroborationSuffix(candidate))}</p>");

            if (decision is "act" or "learn")
            {
                sb.AppendLine($"<p style=\"margin:17px 0 0;\"><a href=\"{Attribute(SafeUrl(article.Link))}\" style=\"display:inline-block;min-height:44px;box-sizing:border-box;padding:12px 18px;border-radius:9px;background:{color};color:#ffffff;font-size:14px;line-height:20px;font-weight:750;text-decoration:none;\">Open source</a></p>");
            }

            AppendFeedback(sb, candidate, brand, generatedAtUtc);
            sb.AppendLine("</td></tr>");
        }
    }

    private static void AppendLabeledParagraph(StringBuilder sb, string label, string value) =>
        sb.AppendLine($"<p class=\"copy\" style=\"margin:13px 0 0;color:#30394c;font-size:15px;line-height:23px;\"><strong style=\"color:#172033;\">{label}:</strong> {Html(value)}</p>");

    private static void AppendFeedback(
        StringBuilder sb,
        ScoredArticle candidate,
        EmailBrandOptions brand,
        DateTimeOffset generatedAtUtc)
    {
        var eventKey = string.IsNullOrWhiteSpace(candidate.EventKey)
            ? EventIdentity.KeyFor(candidate.Article)
            : candidate.EventKey;
        var signals = new[]
        {
            ("Useful", "useful"),
            ("Noise", "noise"),
            ("Wrong", "wrong"),
            ("I acted", "acted")
        };
        var links = signals
            .Select(signal => (signal.Item1, Url: FeedbackTokenService.CreateUrl(
                brand.FeedbackBaseUrl,
                brand.FeedbackSigningKey,
                eventKey,
                signal.Item2,
                generatedAtUtc.AddDays(30))))
            .Where(item => item.Url is not null)
            .ToList();
        if (links.Count == 0)
            return;

        sb.AppendLine("<p class=\"muted\" style=\"margin:18px 0 0;color:#7b8499;font-size:12px;line-height:20px;\">Was this signal right? ");
        for (var i = 0; i < links.Count; i++)
        {
            if (i > 0)
                sb.Append(" &nbsp;·&nbsp; ");
            sb.Append($"<a href=\"{Attribute(links[i].Url!)}\" style=\"color:#5146bd;text-decoration:underline;\">{links[i].Item1}</a>");
        }
        sb.AppendLine("</p>");
    }

    private static List<(BriefingItem Item, ScoredArticle Candidate)> ValidItems(
        string decision,
        IReadOnlyList<ScoredArticle> candidates,
        IEnumerable<BriefingItem> items) =>
        items
            .Where(item => item.Decision == decision)
            .Where(item => item.ArticleIndex >= 1 && item.ArticleIndex <= candidates.Count)
            .Select(item => (item, candidates[item.ArticleIndex - 1]))
            .ToList();

    private static string BuildStatusLine(BriefingDocument briefing)
    {
        var parts = new[] { "act", "watch", "learn" }
            .Select(decision => (Decision: decision, Count: briefing.Items.Count(item => item.Decision == decision)))
            .Where(item => item.Count > 0)
            .Select(item => $"{item.Count} {item.Decision.ToUpperInvariant()}");
        return string.Join(" · ", parts);
    }

    private static string BuildTransparencyLine(int candidateCount, int selectedCount) =>
        $"{candidateCount} candidate event{(candidateCount == 1 ? "" : "s")} scanned · {selectedCount} kept · {Math.Max(0, candidateCount - selectedCount)} suppressed. No quota filling.";

    private static string CorroborationSuffix(ScoredArticle candidate) =>
        candidate.SourceCount > 1 ? $" · corroborated by {candidate.SourceCount} sources" : "";

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

    private static string PossessiveName(string displayName) =>
        displayName.Equals("Your", StringComparison.OrdinalIgnoreCase)
            ? "Your"
            : displayName.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                ? $"{displayName}'"
                : $"{displayName}'s";

    private static string Html(string? value) =>
        WebUtility.HtmlEncode(Compact(value));

    private static string Attribute(string value) => WebUtility.HtmlEncode(value);

    private static string EscapeLinkText(string value) => EscapeMarkdown(value);

    private static string EscapeMarkdown(string? value)
    {
        var escaped = Regex.Replace(
            Compact(value),
            @"[\\`*_\[\]()]",
            match => "\\" + match.Value);
        return WebUtility.HtmlEncode(escaped).Trim();
    }

    private static string Compact(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string SafeUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri.Replace("(", "%28").Replace(")", "%29")
            : "#";
}
