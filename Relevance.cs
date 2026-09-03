using System.Text.RegularExpressions;

public static class ArticleSelector
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "mc_cid", "mc_eid", "ref", "ref_src"
    };

    public static List<ScoredArticle> Rank(
        IEnumerable<NewsItem> articles,
        BriefingProfile profile,
        IEnumerable<string> previouslySentLinks,
        DateTimeOffset now,
        DateTimeOffset? notBefore = null,
        IEnumerable<string>? previouslyReviewedEventKeys = null,
        IEnumerable<string>? previouslyReviewedEventTitles = null)
    {
        var sent = previouslySentLinks
            .Select(CanonicalizeLink)
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reviewedEvents = (previouslyReviewedEventKeys ?? Array.Empty<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reviewedTitles = (previouslyReviewedEventTitles ?? Array.Empty<string>())
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cutoff = notBefore ?? now.AddHours(-profile.LookbackHours);
        var eligible = articles
            .Where(article => article.Published >= cutoff && article.Published <= now.AddHours(2))
            .Where(article => !string.IsNullOrWhiteSpace(article.Link))
            .Where(article => !sent.Contains(CanonicalizeLink(article.Link)))
            .GroupBy(article => CanonicalizeLink(article.Link), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(article => article.Published).First())
            .ToList();

        return EventIdentity.Cluster(eligible, profile.EventSimilarityThreshold)
            .Where(cluster => !cluster.IdentityKeys.Any(reviewedEvents.Contains))
            .Where(cluster => !reviewedTitles.Any(reviewedTitle =>
                EventIdentity.ReviewedVersionCanSuppress(
                    reviewedTitle,
                    cluster.Articles.Select(article => article.Title))
                && cluster.Articles.Any(article =>
                    EventIdentity.TitleSimilarity(article.Title, reviewedTitle)
                        >= profile.EventSimilarityThreshold)))
            .Select(cluster => Score(cluster, profile, now))
            .Where(result => result is not null)
            .Select(result => result!)
            .Where(result => result.Score >= profile.MinimumScore && result.MatchedPriorities.Count > 0)
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.Article.Published)
            .Take(profile.CandidateLimit)
            .ToList();
    }

    public static string CanonicalizeLink(string? link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            return link?.Trim() ?? "";

        var builder = new UriBuilder(uri)
        {
            Fragment = "",
            Host = uri.Host.ToLowerInvariant()
        };
        if (builder.Path.Length > 1)
            builder.Path = builder.Path.TrimEnd('/');

        var kept = builder.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair =>
            {
                var key = Uri.UnescapeDataString(pair.Split('=', 2)[0]);
                return !key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
                    && !TrackingParameters.Contains(key);
            });

        builder.Query = string.Join('&', kept);
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private static ScoredArticle? Score(
        NewsEventCluster cluster,
        BriefingProfile profile,
        DateTimeOffset now)
    {
        var scoredMembers = cluster.Articles
            .Select(article => ScoreArticle(article, profile, now))
            .Where(result => result.MatchedPriorities.Count > 0)
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.Article.Published)
            .ToList();
        if (scoredMembers.Count == 0)
            return null;

        var representative = scoredMembers[0];
        var matchedPriorities = scoredMembers
            .SelectMany(result => result.MatchedPriorities)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var corroborationBoost = Math.Min(1.2, Math.Max(0, cluster.Sources.Count - 1) * 0.4);

        return new ScoredArticle(
            representative.Article,
            Math.Round(representative.Score + corroborationBoost, 3),
            matchedPriorities,
            cluster.EventKey,
            cluster.Sources.Count,
            cluster.Sources,
            cluster.IdentityKeys,
            cluster.IdentityTitles);
    }

    private static ScoredArticle ScoreArticle(NewsItem article, BriefingProfile profile, DateTimeOffset now)
    {
        var title = Normalize(article.Title);
        var summary = Normalize(article.Summary ?? "");
        var matched = new List<string>();
        double score = 0;

        foreach (var priority in profile.Priorities)
        {
            var titleMatches = priority.Signals.Count(signal => ContainsSignal(title, signal));
            var summaryMatches = priority.Signals.Count(signal => ContainsSignal(summary, signal));
            if (titleMatches == 0 && summaryMatches == 0)
                continue;

            matched.Add(priority.Name);
            score += priority.Weight * Math.Min(1.6, (titleMatches * 0.6) + (summaryMatches * 0.25));
        }

        foreach (var region in profile.Regions)
        {
            if (ContainsSignal(title, region) || ContainsSignal(summary, region))
                score += 0.25;
        }

        if (Uri.TryCreate(article.Link, UriKind.Absolute, out var uri)
            && profile.TrustedDomains.Any(domain =>
                uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.75;
        }

        var configuredSource = profile.Sources.FirstOrDefault(source =>
            !string.IsNullOrWhiteSpace(article.FeedUrl)
            && source.Url.Equals(article.FeedUrl, StringComparison.OrdinalIgnoreCase));
        if (configuredSource is not null)
        {
            score += (configuredSource.Trust - 3) * 0.15;
            if (configuredSource.Official)
                score += 0.2;
        }

        var ageHours = Math.Max(0, (now - article.Published).TotalHours);
        var recency = Math.Max(0, 1 - (ageHours / profile.LookbackHours));
        score += recency * 0.75;

        return new ScoredArticle(article, Math.Round(score, 3), matched);
    }

    private static bool ContainsSignal(string normalizedText, string signal)
    {
        var normalizedSignal = Normalize(signal);
        if (normalizedSignal.Length <= 1)
            return false;

        var leftBoundary = char.IsLetterOrDigit(normalizedSignal[0]) ? @"(?<![\p{L}\p{N}])" : "";
        var rightBoundary = char.IsLetterOrDigit(normalizedSignal[^1]) ? @"(?![\p{L}\p{N}])" : "";
        var pattern = $"{leftBoundary}{Regex.Escape(normalizedSignal)}{rightBoundary}";
        return Regex.IsMatch(normalizedText, pattern, RegexOptions.CultureInvariant);
    }

    private static string Normalize(string text) =>
        Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim();
}
