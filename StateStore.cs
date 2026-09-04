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
        return DeserializeFromStorage(
            File.ReadAllText(PathFile),
            Environment.GetEnvironmentVariable("OUTBOX_ENCRYPTION_KEY"));
    }

    public static StateOfWorld DeserializeFromStorage(
        string json,
        string? protectionKey)
    {
        var state = JsonSerializer.Deserialize<StateOfWorld>(json, J) ?? new StateOfWorld();
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
                item.EventPrivateSources ??= new();
            }
        }
        state.DeliveryRetries ??= new();
        state.RecentRuns ??= new();
        RestoreProtectedDurableContent(state, protectionKey);
        SanitizeDurableSourceMetadata(state);
        RestoreFeedValidators(state, protectionKey);
        return state;
    }

    public static void Save(StateOfWorld s)
    {
        SanitizeDurableSourceMetadata(s);
        Directory.CreateDirectory(DataDir);
        var temporaryPath = PathFile + ".tmp";
        File.WriteAllText(
            temporaryPath,
            SerializeForStorage(
                s,
                Environment.GetEnvironmentVariable("OUTBOX_ENCRYPTION_KEY")));
        File.Move(temporaryPath, PathFile, true);
    }

    public static string SerializeForStorage(
        StateOfWorld state,
        string? validatorProtectionKey)
    {
        SanitizeDurableSourceMetadata(state);
        var storageState = JsonSerializer.Deserialize<StateOfWorld>(
            JsonSerializer.Serialize(state, J),
            J) ?? throw new InvalidOperationException("Unable to create a protected state snapshot.");
        foreach (var health in storageState.FeedHealth)
        {
            health.ETag = DurableSecretProtection.Protect(
                health.ETag,
                validatorProtectionKey);
        }
        ProtectDurableArticleContent(storageState, validatorProtectionKey);
        return JsonSerializer.Serialize(storageState, J);
    }

    public static void AppendNews(StateOfWorld s, IEnumerable<NewsItem> items, int keepDays = 4)
    {
        var incoming = items.Select(SourceIdentity.Sanitize).ToList();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-keepDays);
        s.CacheNews = incoming
            .Concat(s.CacheNews.Select(SourceIdentity.Sanitize))
            .Where(item => item.Published >= cutoff)
            .Where(item => !string.IsNullOrWhiteSpace(item.Link))
            .GroupBy(item => ArticleSelector.CanonicalizeLink(item.Link), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Published)
                .ThenByDescending(item => string.IsNullOrWhiteSpace(item.FeedUrl) ? 0 : 1)
                .First())
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
            .Select(candidate => SourceIdentity.SanitizeArticleLink(candidate.Article.Link))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedEvents = included
            .SelectMany(candidate => candidate.ReviewEventKeys)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateList = candidates.ToList();

        state.ReviewedArticles.AddRange(candidateList.Select(candidate =>
        {
            var link = SourceIdentity.SanitizeArticleLink(candidate.Article.Link);
            return new ReviewedArticle(
                link,
                reviewedAtUtc,
                includedLinks.Contains(link),
                deliveryEmailId,
                SourceIdentity.Sanitize(candidate.Article).PrivateSource);
        }));
        state.ReviewedEvents.AddRange(candidateList.SelectMany(candidate =>
            ResolveEventIdentities(candidate)
                .Where(identity => !string.IsNullOrWhiteSpace(identity.EventKey))
                .Select(identity => new ReviewedEvent(
                    identity.EventKey,
                    reviewedAtUtc,
                    includedEvents.Contains(identity.EventKey),
                    identity.Title,
                    deliveryEmailId,
                    identity.PrivateSource))));

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
            .Where(item => !string.IsNullOrWhiteSpace(item.Url))
            .GroupBy(item => SourceIdentity.NormalizePersisted(item.Url), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var result in results)
        {
            var sourceIdentity = SourceIdentity.ForUrl(result.Source.Url);
            var health = existing.GetValueOrDefault(sourceIdentity) ?? new FeedHealthState();
            health.Name = SourceIdentity.PublicLabel(sourceIdentity);
            health.Url = sourceIdentity;
            health.LastAttemptUtc = attemptedAtUtc;
            if (result.Status == "ok")
            {
                health.ETag = result.ETag;
                health.LastModifiedUtc = result.LastModifiedUtc;
            }
            else if (result.Status == "not_modified")
            {
                health.ETag = result.ETag ?? health.ETag;
                health.LastModifiedUtc = result.LastModifiedUtc ?? health.LastModifiedUtc;
            }

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
                health.LastError = SourceIdentity.RedactFrom(result.Error, result.Source.Url);
                health.LastItemCount = 0;
            }

            existing[sourceIdentity] = health;
        }

        state.FeedHealth = existing.Values
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void RecordDelivery(StateOfWorld state, DeliveryAttempt delivery)
    {
        var sanitized = delivery with
        {
            IncludedItems = delivery.IncludedItems?
                .Select(SourceIdentity.Sanitize)
                .Where(item => !string.IsNullOrWhiteSpace(item.Link))
                .ToList()
        };
        state.Deliveries.Add(sanitized);
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
            .Select(item => SourceIdentity.SanitizeArticleLink(item.Article.Link))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        state.DeliveryRetries.AddRange(articles
            .Where(article => !string.IsNullOrWhiteSpace(article.Link))
            .Select(SourceIdentity.Sanitize)
            .Where(article => !string.IsNullOrWhiteSpace(article.Link))
            .Select(article => new DeliveryRetryItem(article, queuedAtUtc)));
        state.DeliveryRetries = state.DeliveryRetries
            .GroupBy(item => SourceIdentity.SanitizeArticleLink(item.Article.Link), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.QueuedAtUtc).First())
            .OrderByDescending(item => item.QueuedAtUtc)
            .ToList();
        return state.DeliveryRetries.Any(item =>
            !before.Contains(SourceIdentity.SanitizeArticleLink(item.Article.Link)));
    }

    public static void CompleteDeliveryRetries(
        StateOfWorld state,
        IEnumerable<ScoredArticle> delivered)
    {
        var deliveredList = delivered.ToList();
        var links = deliveredList
            .Select(item => SourceIdentity.SanitizeArticleLink(item.Article.Link))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var eventKeys = deliveredList
            .SelectMany(item => item.ReviewEventKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        state.DeliveryRetries.RemoveAll(item =>
            links.Contains(SourceIdentity.SanitizeArticleLink(item.Article.Link))
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

    private static void SanitizeDurableSourceMetadata(StateOfWorld state)
    {
        state.CacheNews = state.CacheNews
            .Select(SourceIdentity.Sanitize)
            .Where(item => !string.IsNullOrWhiteSpace(item.Link))
            .ToList();

        state.ReviewedArticles = state.ReviewedArticles
            .Select(item => item with
            {
                Link = SourceIdentity.SanitizeArticleLink(item.Link)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Link))
            .ToList();

        foreach (var health in state.FeedHealth)
        {
            var identity = SourceIdentity.NormalizePersisted(health.Url);
            health.Url = identity;
            health.Name = SourceIdentity.PublicLabel(identity);
        }

        state.Deliveries = state.Deliveries.Select(delivery => delivery with
        {
            IncludedItems = delivery.IncludedItems?
                .Select(SourceIdentity.Sanitize)
                .Where(item => !string.IsNullOrWhiteSpace(item.Link))
                .ToList()
        }).ToList();

        state.DeliveryRetries = state.DeliveryRetries
            .Select(item => item with { Article = SourceIdentity.Sanitize(item.Article) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Article.Link))
            .ToList();

        foreach (var pending in state.PendingDigestSends)
        {
            foreach (var item in pending.ReviewedItems)
                item.Article = SourceIdentity.Sanitize(item.Article);
        }
    }

    private static void ProtectDurableArticleContent(
        StateOfWorld state,
        string? protectionKey)
    {
        state.CacheNews = state.CacheNews
            .Select(article => ProtectArticle(article, protectionKey))
            .ToList();
        state.ReviewedArticles = state.ReviewedArticles
            .Select(item => item.PrivateSource
                ? item with { Link = ProtectText(item.Link, protectionKey) }
                : item)
            .ToList();
        state.ReviewedEvents = state.ReviewedEvents
            .Select(item => item.PrivateSource
                ? item with { Title = ProtectText(item.Title, protectionKey) }
                : item)
            .ToList();
        state.Deliveries = state.Deliveries
            .Select(delivery => delivery with
            {
                IncludedItems = delivery.IncludedItems?
                    .Select(article => ProtectArticle(article, protectionKey))
                    .ToList()
            })
            .ToList();
        state.DeliveryRetries = state.DeliveryRetries
            .Select(item => item with
            {
                Article = ProtectArticle(item.Article, protectionKey)
            })
            .ToList();
        foreach (var health in state.FeedHealth)
        {
            if (health.LastError is not null)
                health.LastError = ProtectText(health.LastError, protectionKey);
        }

        foreach (var pending in state.PendingDigestSends)
        {
            pending.EventTitles = pending.EventTitles
                .Select(title => ProtectText(title, protectionKey))
                .ToList();
            foreach (var item in pending.ReviewedItems)
            {
                item.Article = ProtectArticle(item.Article, protectionKey);
                item.EventTitles = item.EventTitles
                    .Select(title => ProtectText(title, protectionKey))
                    .ToList();
            }
        }
    }

    private static void RestoreProtectedDurableContent(
        StateOfWorld state,
        string? protectionKey)
    {
        state.CacheNews = state.CacheNews
            .Select(article => RestoreArticle(article, protectionKey))
            .ToList();
        state.ReviewedArticles = state.ReviewedArticles
            .Select(item => item.PrivateSource
                ? item with { Link = RestoreText(item.Link, protectionKey) }
                : item)
            .ToList();
        state.ReviewedEvents = state.ReviewedEvents
            .Select(item => item.PrivateSource
                ? item with { Title = RestoreText(item.Title, protectionKey) }
                : item)
            .ToList();
        state.Deliveries = state.Deliveries
            .Select(delivery => delivery with
            {
                IncludedItems = delivery.IncludedItems?
                    .Select(article => RestoreArticle(article, protectionKey))
                    .ToList()
            })
            .ToList();
        state.DeliveryRetries = state.DeliveryRetries
            .Select(item => item with
            {
                Article = RestoreArticle(item.Article, protectionKey)
            })
            .ToList();
        foreach (var health in state.FeedHealth)
        {
            if (health.LastError is not null)
                health.LastError = RestoreText(health.LastError, protectionKey);
        }

        foreach (var pending in state.PendingDigestSends)
        {
            pending.EventTitles = pending.EventTitles
                .Select(title => RestoreText(title, protectionKey))
                .ToList();
            foreach (var item in pending.ReviewedItems)
            {
                item.Article = RestoreArticle(item.Article, protectionKey);
                item.EventTitles = item.EventTitles
                    .Select(title => RestoreText(title, protectionKey))
                    .ToList();
            }
        }
    }

    private static NewsItem ProtectArticle(NewsItem article, string? protectionKey) =>
        !article.PrivateSource
            ? article
            : article with
            {
                Title = ProtectText(article.Title, protectionKey),
                Link = ProtectText(article.Link, protectionKey),
                Summary = article.Summary is null
                    ? null
                    : ProtectText(article.Summary, protectionKey)
            };

    private static NewsItem RestoreArticle(NewsItem article, string? protectionKey) =>
        !article.PrivateSource
            ? article
            : article with
            {
                Title = RestoreText(article.Title, protectionKey),
                Link = RestoreText(article.Link, protectionKey),
                Summary = article.Summary is null
                    ? null
                    : RestoreText(article.Summary, protectionKey)
            };

    private static string ProtectText(string value, string? protectionKey) =>
        DurableSecretProtection.Protect(value, protectionKey) ?? "";

    private static string RestoreText(string value, string? protectionKey) =>
        DurableSecretProtection.Unprotect(value, protectionKey) ?? "";

    private static IEnumerable<(string EventKey, string Title, bool PrivateSource)> ResolveEventIdentities(
        ScoredArticle candidate)
    {
        if (candidate.IdentityKeys is { Count: > 0 } keys
            && candidate.IdentityTitles is { Count: > 0 } titles
            && keys.Count == titles.Count)
        {
            var privacy = candidate.IdentityPrivateSources is { Count: > 0 } privateSources
                && privateSources.Count == keys.Count
                    ? privateSources
                    : Enumerable.Repeat(
                        SourceIdentity.Sanitize(candidate.Article).PrivateSource,
                        keys.Count).ToList();
            return keys.Select((key, index) => (key, titles[index], privacy[index]));
        }

        var privateSource = SourceIdentity.Sanitize(candidate.Article).PrivateSource;
        return candidate.ReviewEventKeys.Select(key =>
            (key, candidate.Article.Title, privateSource));
    }

    private static void RestoreFeedValidators(
        StateOfWorld state,
        string? validatorProtectionKey)
    {
        foreach (var health in state.FeedHealth)
        {
            health.ETag = DurableSecretProtection.Unprotect(
                health.ETag,
                validatorProtectionKey);
        }
    }
}
