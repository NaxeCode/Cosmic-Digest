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
        return JsonSerializer.Deserialize<StateOfWorld>(File.ReadAllText(PathFile), J) ?? new StateOfWorld();
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
        IEnumerable<NewsItem> candidates,
        IEnumerable<NewsItem> included,
        DateTimeOffset reviewedAtUtc)
    {
        var includedLinks = included
            .Select(article => ArticleSelector.CanonicalizeLink(article.Link))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        state.ReviewedArticles.AddRange(candidates.Select(article =>
        {
            var link = ArticleSelector.CanonicalizeLink(article.Link);
            return new ReviewedArticle(link, reviewedAtUtc, includedLinks.Contains(link));
        }));

        var cutoff = reviewedAtUtc.AddDays(-45);
        state.ReviewedArticles = state.ReviewedArticles
            .Where(item => item.ReviewedAtUtc >= cutoff)
            .GroupBy(item => item.Link, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.ReviewedAtUtc).First())
            .OrderByDescending(item => item.ReviewedAtUtc)
            .ToList();
    }
}
