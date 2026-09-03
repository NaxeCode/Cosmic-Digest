// StateStore.cs
using System.Text.Json;

public static class StateStore
{
    static readonly string DataDir = Environment.GetEnvironmentVariable("DATA_DIR") ?? "./data";
    static readonly string PathFile = Path.Combine(DataDir, "state.json");
    static readonly JsonSerializerOptions J = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static StateOfWorld Load()
    {
        if (!File.Exists(PathFile)) return new StateOfWorld();
        var state = JsonSerializer.Deserialize<StateOfWorld>(File.ReadAllText(PathFile), J) ?? new StateOfWorld();
        state.CacheNews ??= new();
        state.ReviewedArticles ??= new();
        state.ReviewedEvents ??= new();
        state.FeedHealth ??= new();
        state.Deliveries ??= new();
        state.PendingDigestSends ??= new();
        foreach (var pending in state.PendingDigestSends)
        {
            pending.EventKeys ??= new();
            pending.EventTitles ??= new();
            pending.PayloadNonce ??= "";
            pending.PayloadCiphertext ??= "";
            pending.PayloadTag ??= "";
            pending.ReviewedItems ??= new();
            foreach (var item in pending.ReviewedItems)
            {
                item.EventKeys ??= new();
                item.EventTitles ??= new();
            }
        }
        state.DeliveryRetries ??= new();
        state.RecentRuns ??= new();
        return state;
    }

    public static void Save(StateOfWorld s)
    {
        Directory.CreateDirectory(DataDir);
        var temporaryPath = PathFile + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(s, J));
        File.Move(temporaryPath, PathFile, true);
    }

    public static void AppendNews(StateOfWorld s, IEnumerable<NewsItem> items, int keepDays = 4)
    {
        s.CacheNews.AddRange(items);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-keepDays);
        s.CacheNews = s.CacheNews
            .Where(item => item.Published >= cutoff)
            .Where(item => !string.IsNullOrWhiteSpace(item.Link))
            .GroupBy(item => ArticleSelector.CanonicalizeLink(item.Link), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Published).First())
            .OrderByDescending(item => item.Published)
            .ToList();
    }

    public static void MarkReviewed(
        StateOfWorld state,
        IEnumerable<ScoredArticle> candidates,
        IEnumerable<ScoredArticle> included,
        DateTimeOffset reviewedAtUtc,
        string? deliveryEmailId = null)
    {
        var includedLinks = included
            .Select(candidate => ArticleSelector.CanonicalizeLink(candidate.Article.Link))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedEvents = included
            .SelectMany(candidate => candidate.ReviewEventKeys)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateList = candidates.ToList();

        state.ReviewedArticles.AddRange(candidateList.Select(candidate =>
        {
            var link = ArticleSelector.CanonicalizeLink(candidate.Article.Link);
            return new ReviewedArticle(
                link,
                reviewedAtUtc,
                includedLinks.Contains(link),
                deliveryEmailId);
        }));
        state.ReviewedEvents.AddRange(candidateList.SelectMany(candidate =>
            ResolveEventIdentities(candidate)
                .Where(identity => !string.IsNullOrWhiteSpace(identity.EventKey))
                .Select(identity => new ReviewedEvent(
                    identity.EventKey,
                    reviewedAtUtc,
                    includedEvents.Contains(identity.EventKey),
                    identity.Title,
                    deliveryEmailId))));

        PruneReviewed(state, reviewedAtUtc);
    }

    public static void PruneReviewed(StateOfWorld state, DateTimeOffset now, int keepDays = 45)
    {
        var cutoff = now.AddDays(-keepDays);
        state.ReviewedArticles = state.ReviewedArticles
            .Where(item => item.ReviewedAtUtc >= cutoff)
            .GroupBy(item => item.Link, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.ReviewedAtUtc).First())
            .OrderByDescending(item => item.ReviewedAtUtc)
            .ToList();
        state.ReviewedEvents = state.ReviewedEvents
            .Where(item => item.ReviewedAtUtc >= cutoff)
            .Where(item => !string.IsNullOrWhiteSpace(item.EventKey))
            .GroupBy(item => item.EventKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.ReviewedAtUtc).First())
            .OrderByDescending(item => item.ReviewedAtUtc)
            .ToList();
    }

    public static void UpdateFeedHealth(
        StateOfWorld state,
        IEnumerable<FeedFetchResult> results,
        DateTimeOffset attemptedAtUtc)
    {
        var existing = state.FeedHealth
            .GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var result in results)
        {
            var health = existing.GetValueOrDefault(result.Source.Url) ?? new FeedHealthState();
            health.Name = result.Source.Name;
            health.Url = result.Source.Url;
            health.LastAttemptUtc = attemptedAtUtc;
            health.ETag = result.ETag ?? health.ETag;
            health.LastModifiedUtc = result.LastModifiedUtc ?? health.LastModifiedUtc;
            if (result.IsHealthy)
            {
                health.LastSuccessUtc = attemptedAtUtc;
                health.ConsecutiveFailures = 0;
                health.LastError = null;
                if (result.Status == "ok")
                    health.LastItemCount = result.Items.Count;
            }
            else if (result.Status == "failed")
            {
                health.LastFailureUtc = attemptedAtUtc;
                health.ConsecutiveFailures++;
                health.LastError = result.Error;
                health.LastItemCount = 0;
            }

            existing[result.Source.Url] = health;
        }

        state.FeedHealth = existing.Values
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void RecordDelivery(StateOfWorld state, DeliveryAttempt delivery)
    {
        state.Deliveries.Add(delivery);
        state.Deliveries = state.Deliveries
            .GroupBy(item => item.EmailId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.StatusAtUtc).First())
            .OrderByDescending(item => item.SentAtUtc)
            .Take(45)
            .ToList();
    }

    public static bool RestoreEligibilityForFailedDelivery(
        StateOfWorld state,
        DeliveryAttempt delivery,
        DateTimeOffset queuedAtUtc)
    {
        var emailId = delivery.EmailId;
        if (string.IsNullOrWhiteSpace(emailId))
            return false;

        var removedArticles = state.ReviewedArticles.RemoveAll(item =>
            item.Included
            && string.Equals(item.DeliveryEmailId, emailId, StringComparison.OrdinalIgnoreCase));
        var removedEvents = state.ReviewedEvents.RemoveAll(item =>
            item.Included
            && string.Equals(item.DeliveryEmailId, emailId, StringComparison.OrdinalIgnoreCase));
        var queued = QueueDeliveryRetries(state, delivery.IncludedItems, queuedAtUtc);
        return removedArticles > 0 || removedEvents > 0 || queued;
    }

    public static bool QueueDeliveryRetries(
        StateOfWorld state,
        IEnumerable<NewsItem>? articles,
        DateTimeOffset queuedAtUtc)
    {
        if (articles is null)
            return false;

        var before = state.DeliveryRetries
            .Select(item => ArticleSelector.CanonicalizeLink(item.Article.Link))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        state.DeliveryRetries.AddRange(articles
            .Where(article => !string.IsNullOrWhiteSpace(article.Link))
            .Select(article => new DeliveryRetryItem(article, queuedAtUtc)));
        state.DeliveryRetries = state.DeliveryRetries
            .GroupBy(item => ArticleSelector.CanonicalizeLink(item.Article.Link), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.QueuedAtUtc).First())
            .OrderByDescending(item => item.QueuedAtUtc)
            .Take(50)
            .ToList();
        return state.DeliveryRetries.Any(item =>
            !before.Contains(ArticleSelector.CanonicalizeLink(item.Article.Link)));
    }

    public static void CompleteDeliveryRetries(
        StateOfWorld state,
        IEnumerable<ScoredArticle> delivered)
    {
        var deliveredList = delivered.ToList();
        var links = deliveredList
            .Select(item => ArticleSelector.CanonicalizeLink(item.Article.Link))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var eventKeys = deliveredList
            .SelectMany(item => item.ReviewEventKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        state.DeliveryRetries.RemoveAll(item =>
            links.Contains(ArticleSelector.CanonicalizeLink(item.Article.Link))
            || eventKeys.Contains(EventIdentity.KeyFor(item.Article)));
    }

    public static void RecordRun(StateOfWorld state, RunMetrics metrics)
    {
        state.RecentRuns.Add(metrics);
        state.RecentRuns = state.RecentRuns
            .OrderByDescending(item => item.RunAtUtc)
            .Take(45)
            .ToList();
    }

    public static DateTimeOffset ResolveCandidateCutoff(
        StateOfWorld state,
        DateTimeOffset now,
        int lookbackHours)
    {
        var lookbackCutoff = now.AddHours(-lookbackHours);
        if (state.LegacyMigrationNotBeforeUtc is null
            && state.ReviewedArticles.Count == 0
            && state.LastDigestUtc is not null)
        {
            state.LegacyMigrationNotBeforeUtc = state.LastDigestUtc.Value.AddHours(-3);
        }

        if (state.LegacyMigrationNotBeforeUtc <= lookbackCutoff)
            state.LegacyMigrationNotBeforeUtc = null;

        return state.LegacyMigrationNotBeforeUtc is { } migrationCutoff
            && migrationCutoff > lookbackCutoff
                ? migrationCutoff
                : lookbackCutoff;
    }

    private static IEnumerable<(string EventKey, string Title)> ResolveEventIdentities(
        ScoredArticle candidate)
    {
        if (candidate.IdentityKeys is { Count: > 0 } keys
            && candidate.IdentityTitles is { Count: > 0 } titles
            && keys.Count == titles.Count)
        {
            return keys.Zip(titles, (key, title) => (key, title));
        }

        return candidate.ReviewEventKeys.Select(key => (key, candidate.Article.Title));
    }

}
