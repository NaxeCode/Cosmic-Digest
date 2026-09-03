using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public sealed record NewsEventCluster(
    string EventKey,
    IReadOnlyList<NewsItem> Articles,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> IdentityKeys);

public static partial class EventIdentity
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "has", "have", "how",
        "in", "into", "is", "it", "its", "new", "of", "on", "or", "that", "the", "their", "this",
        "to", "up", "was", "what", "when", "with", "you", "your"
    };

    public static IReadOnlyList<NewsEventCluster> Cluster(
        IEnumerable<NewsItem> articles,
        double similarityThreshold)
    {
        var clusters = new List<List<NewsItem>>();
        foreach (var article in articles.OrderByDescending(item => item.Published))
        {
            var articleTokens = Tokens(article.Title);
            var bestCluster = -1;
            var bestSimilarity = 0d;

            for (var i = 0; i < clusters.Count; i++)
            {
                var similarity = clusters[i]
                    .Select(member => CanCluster(article.Title, member.Title)
                        ? Similarity(articleTokens, Tokens(member.Title))
                        : 0)
                    .DefaultIfEmpty(0)
                    .Max();
                if (similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    bestCluster = i;
                }
            }

            if (bestCluster >= 0 && bestSimilarity >= similarityThreshold)
                clusters[bestCluster].Add(article);
            else
                clusters.Add(new List<NewsItem> { article });
        }

        return clusters.Select(cluster =>
        {
            var identityKeys = cluster
                .Select(KeyFor)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToList();
            var eventKey = identityKeys.First();
            var sources = cluster
                .Select(article => article.Source)
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new NewsEventCluster(eventKey, cluster, sources, identityKeys);
        }).ToList();
    }

    public static string KeyFor(NewsItem article)
    {
        var seed = Signature(article.Title);
        if (seed.Length == 0)
            seed = ArticleSelector.CanonicalizeLink(article.Link);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..24]
            .ToLowerInvariant();
    }

    public static string Signature(string title) =>
        string.Join(' ', Tokens(title).Order(StringComparer.Ordinal));

    public static double TitleSimilarity(string left, string right) =>
        CanCluster(left, right) ? Similarity(Tokens(left), Tokens(right)) : 0;

    private static bool CanCluster(string left, string right)
    {
        var leftNumeric = NumericIdentityTokens(left);
        var rightNumeric = NumericIdentityTokens(right);
        return leftNumeric.Count == 0
            || rightNumeric.Count == 0
            || leftNumeric.SetEquals(rightNumeric);
    }

    private static HashSet<string> NumericIdentityTokens(string value) =>
        Tokens(value)
            .Where(token => token.Any(char.IsDigit))
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> Tokens(string value) =>
        TokenPattern().Matches(value.ToLowerInvariant()).Cast<Match>()
            .Select(match => match.Value.Trim('.', '-', '_'))
            .Where(token => token.Length >= 2 || token.Any(char.IsDigit))
            .Where(token => !StopWords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);

    private static double Similarity(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
            return 0;

        var intersection = left.Count(right.Contains);
        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}+#._-]*", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
