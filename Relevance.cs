using System.Text.RegularExpressions;

public static class ArticleSelector
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "mc_cid", "mc_eid", "ref", "ref_src"
    };

    private sealed record PreclusterCandidate(
        NewsItem Article,
        bool IsForcedRetry,
        ScoredArticle PreScore,
        string SourceKey);

    public static List<ScoredArticle> Rank(
        IEnumerable<NewsItem> articles,
        BriefingProfile profile,
        IEnumerable<string> previouslySentLinks,
        DateTimeOffset now,
        DateTimeOffset? notBefore = null,
        IEnumerable<string>? previouslyReviewedEventKeys = null,
        IEnumerable<string>? previouslyReviewedEventTitles = null,
        IEnumerable<string>? forcedRetryLinks = null)
    {
        var sent = previouslySentLinks
            .Select(ComparisonLink)
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reviewedEvents = (previouslyReviewedEventKeys ?? Array.Empty<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reviewedTitles = (previouslyReviewedEventTitles ?? Array.Empty<string>())
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var reviewedTitlesByKey = reviewedTitles
            .Select(title => new { Key = EventIdentity.KeyForTitle(title), Title = title })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Title).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var forcedRetries = (forcedRetryLinks ?? Array.Empty<string>())
            .Select(ComparisonLink)
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var cutoff = notBefore ?? now.AddHours(-profile.LookbackHours);
        var clusterInputLimit = Math.Clamp(profile.CandidateLimit * 12, 120, 480);
        var precluster = articles
            .Where(article => !string.IsNullOrWhiteSpace(article.Link))
            .Where(article =>
                (article.Published >= cutoff
                    || forcedRetries.Contains(ComparisonLink(article.Link)))
                && article.Published <= now.AddHours(2))
            .Where(article => !sent.Contains(ComparisonLink(article.Link)))
            .GroupBy(article => ComparisonLink(article.Link), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(article => article.Published).First())
            .Select(article => SourceIdentity.Rehydrate(article, profile))
            .Select(article => new PreclusterCandidate(
                article,
                forcedRetries.Contains(ComparisonLink(article.Link)),
                ScoreArticle(article, profile, now),
                ResolveSourceKey(article)))
            .ToList();
        var eligible = SelectFairClusterInput(precluster, clusterInputLimit)
            .Select(item => item.Article)
            .ToList();

        return EventIdentity.Cluster(eligible, profile.EventSimilarityThreshold)
            .Where(cluster => !IsSuppressedByReviewedIdentity(
                cluster,
                reviewedEvents,
                reviewedTitlesByKey))
            .Where(cluster => !reviewedTitles.Any(reviewedTitle =>
                EventIdentity.ReviewedVersionCanSuppress(
                    reviewedTitle,
                    cluster.Articles.Select(article => article.Title))
                && cluster.Articles.Any(article =>
                    EventIdentity.TitleSimilarity(article.Title, reviewedTitle)
                        >= profile.EventSimilarityThreshold)))
            .Select(cluster => new
            {
                Result = Score(
                    cluster,
                    profile,
                    now,
                    cluster.Articles.Any(article =>
                        forcedRetries.Contains(ComparisonLink(article.Link)))),
                IsForcedRetry = cluster.Articles.Any(article =>
                    forcedRetries.Contains(ComparisonLink(article.Link)))
            })
            .Where(item => item.Result is not null)
            .Select(item => new { Result = item.Result!, item.IsForcedRetry })
            .Where(item => item.IsForcedRetry
                || (item.Result.Score >= profile.MinimumScore
                    && item.Result.MatchedPriorities.Count > 0))
            .OrderByDescending(item => item.IsForcedRetry)
            .ThenByDescending(item => item.Result.Score)
            .ThenByDescending(item => item.Result.Article.Published)
            .Take(profile.CandidateLimit)
            .Select(item => item.Result)
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

    private static string ComparisonLink(string? link) =>
        SourceIdentity.SanitizeArticleLink(CanonicalizeLink(link));

    private static IReadOnlyList<PreclusterCandidate> SelectFairClusterInput(
        IReadOnlyList<PreclusterCandidate> candidates,
        int limit)
    {
        if (candidates.Count <= limit)
            return candidates
                .OrderByDescending(item => item.IsForcedRetry)
                .ThenByDescending(item => item.PreScore.MatchedPriorities.Count > 0)
                .ThenByDescending(item => item.PreScore.Score)
                .ThenByDescending(item => item.Article.Published)
                .ToList();

        var selected = candidates
            .Where(item => item.IsForcedRetry)
            .OrderByDescending(item => item.Article.Published)
            .Take(limit)
            .ToList();
        if (selected.Count >= limit)
            return selected;

        var groups = candidates
            .Where(item => !item.IsForcedRetry)
            .GroupBy(item => item.SourceKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new Queue<PreclusterCandidate>(group
                .GroupBy(
                    item => EventIdentity.Signature(item.Article.Title),
                    StringComparer.OrdinalIgnoreCase)
                .Select(signatureGroup => signatureGroup
                    .OrderByDescending(item => item.PreScore.MatchedPriorities.Count > 0)
                    .ThenByDescending(item => item.PreScore.Score)
                    .ThenByDescending(item => item.Article.Published)
                    .First())
                .OrderByDescending(item => item.PreScore.MatchedPriorities.Count > 0)
                .ThenByDescending(item => item.PreScore.Score)
                .ThenByDescending(item => item.Article.Published)))
            .Where(queue => queue.Count > 0)
            .OrderByDescending(queue => queue.Peek().PreScore.MatchedPriorities.Count > 0)
            .ThenByDescending(queue => queue.Peek().PreScore.Score)
            .ToList();

        while (selected.Count < limit && groups.Any(queue => queue.Count > 0))
        {
            foreach (var queue in groups)
            {
                if (selected.Count >= limit)
                    break;
                if (queue.Count > 0)
                    selected.Add(queue.Dequeue());
            }
        }

        return selected;
    }

    private static string ResolveSourceKey(NewsItem article) =>
        !string.IsNullOrWhiteSpace(article.FeedUrl)
            ? SourceIdentity.NormalizePersisted(article.FeedUrl)
            : article.Source;

    private static bool IsSuppressedByReviewedIdentity(
        NewsEventCluster cluster,
        IReadOnlySet<string> reviewedEvents,
        IReadOnlyDictionary<string, List<string>> reviewedTitlesByKey)
    {
        var incomingTitles = cluster.Articles.Select(article => article.Title).ToList();
        foreach (var matchingKey in cluster.IdentityKeys.Where(reviewedEvents.Contains))
        {
            if (reviewedTitlesByKey.TryGetValue(matchingKey, out var matchingTitles))
            {
                if (matchingTitles.Any(title =>
                    EventIdentity.ReviewedVersionCanSuppress(title, incomingTitles)))
                {
                    return true;
                }
                continue;
            }

            if (cluster.IdentityKeys.All(reviewedEvents.Contains))
                return true;
        }

        return false;
    }

    private static ScoredArticle? Score(
        NewsEventCluster cluster,
        BriefingProfile profile,
        DateTimeOffset now,
        bool forcedRetry)
    {
        var scoredMembers = cluster.Articles
            .Select(article => ScoreArticle(article, profile, now))
            .Where(result => forcedRetry || result.MatchedPriorities.Count > 0)
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
            SourceIdentity.Matches(article.FeedUrl, source.Url));
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
