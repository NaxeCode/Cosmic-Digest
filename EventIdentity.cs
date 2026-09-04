using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public sealed record NewsEventCluster(
    string EventKey,
    IReadOnlyList<NewsItem> Articles,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> IdentityKeys,
    IReadOnlyList<string> IdentityTitles);

public static partial class EventIdentity
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "has", "have", "how",
        "in", "into", "is", "it", "its", "new", "of", "on", "or", "that", "the", "their", "this",
        "to", "up", "was", "what", "when", "with", "you", "your"
    };

    private static readonly HashSet<string> GenericVersionMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "version", "ver", "release", "released", "update", "updated", "build", "preview", "beta", "alpha",
        "stable", "rc", "edition"
    };

    private static readonly HashSet<string> StrongVersionMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "version", "ver", "build", "edition"
    };

    private static readonly HashSet<string> IncidentalCalendarContexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "ces", "wwdc", "gdc", "conference", "conf", "summit", "keynote", "expo", "event",
        "january", "february", "march", "april", "may", "june", "july", "august", "september",
        "october", "november", "december", "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep",
        "sept", "oct", "nov", "dec", "today", "tomorrow", "yesterday", "roadmap", "forecast"
    };

    private static readonly HashSet<string> NarrativeNumberContexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "announces", "announced", "launches", "launched", "releases", "released", "reports", "reported",
        "says", "said", "adds", "added", "cuts", "cut", "raises", "raised", "ships", "shipped", "unveils",
        "unveiled", "shows", "showed", "targets", "targeted", "expects", "expected"
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
                if (!clusters[i].All(member => CanCluster(article.Title, member.Title)))
                    continue;

                var similarity = clusters[i]
                    .Select(member => Similarity(articleTokens, Tokens(member.Title)))
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
            var identities = cluster
                .Select(article => new { Key = KeyFor(article), article.Title })
                .GroupBy(identity => identity.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(identity => identity.Key, StringComparer.Ordinal)
                .ToList();
            var identityKeys = identities.Select(identity => identity.Key).ToList();
            var identityTitles = identities.Select(identity => identity.Title).ToList();
            var eventKey = identityKeys.First();
            var sources = cluster
                .Select(article => article.Source)
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new NewsEventCluster(eventKey, cluster, sources, identityKeys, identityTitles);
        }).ToList();
    }

    public static string KeyFor(NewsItem article)
    {
        var titleKey = KeyForTitle(article.Title);
        if (!string.IsNullOrWhiteSpace(titleKey))
            return titleKey;

        return HashSeed(ArticleSelector.CanonicalizeLink(article.Link));
    }

    public static string KeyForTitle(string title)
    {
        var seed = Signature(title);
        return seed.Length == 0 ? "" : HashSeed(seed);
    }

    public static string Signature(string title) =>
        string.Join(' ', Tokens(title).Order(StringComparer.Ordinal));

    public static double TitleSimilarity(string left, string right) =>
        CanCluster(left, right) ? Similarity(Tokens(left), Tokens(right)) : 0;

    public static bool ReviewedVersionCanSuppress(
        string reviewedTitle,
        IEnumerable<string> incomingTitles)
    {
        var reviewedNumeric = NumericIdentityTokens(reviewedTitle);
        var incomingNumeric = incomingTitles
            .Select(NumericIdentityTokens)
            .Where(tokens => tokens.Count > 0)
            .ToList();
        return incomingNumeric.Count == 0
            || (reviewedNumeric.Count > 0
                && incomingNumeric.All(tokens => tokens.SetEquals(reviewedNumeric)));
    }

    private static bool CanCluster(string left, string right)
    {
        var leftNumeric = NumericIdentityTokens(left);
        var rightNumeric = NumericIdentityTokens(right);
        return leftNumeric.Count == 0
            || rightNumeric.Count == 0
            || leftNumeric.SetEquals(rightNumeric);
    }

    private static HashSet<string> NumericIdentityTokens(string value)
    {
        var sanitized = PercentagePattern().Replace(value, " ");
        sanitized = CurrencyNumberPattern().Replace(sanitized, " ");
        sanitized = MultiplierPattern().Replace(sanitized, " ");
        sanitized = QuantityPattern().Replace(sanitized, " ");
        var tokens = OrderedTokens(sanitized);
        var identities = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < tokens.Count; i++)
        {
            var numericToken = tokens[i];
            if (!numericToken.Any(char.IsDigit) || IsIncidentalPeriodToken(numericToken))
                continue;

            var strongVersionContext = HasStrongVersionMarkerBefore(tokens, i);
            var precedingContext = FindPrecedingContext(tokens, i);
            if (precedingContext is not null
                && NarrativeNumberContexts.Contains(precedingContext))
            {
                continue;
            }

            if (IsLikelyCalendarYear(numericToken)
                && precedingContext is not null
                && IncidentalCalendarContexts.Contains(precedingContext))
            {
                continue;
            }

            var productToken = precedingContext;
            if (productToken is null || GenericVersionMarkers.Contains(productToken))
            {
                if (!strongVersionContext)
                    continue;
                productToken = FindFollowingProductToken(tokens, i);
            }

            if (string.IsNullOrWhiteSpace(productToken)
                || productToken.Any(char.IsDigit)
                || NarrativeNumberContexts.Contains(productToken)
                || (IsLikelyCalendarYear(numericToken)
                    && IncidentalCalendarContexts.Contains(productToken)))
            {
                continue;
            }

            identities.Add(numericToken);
            identities.Add($"{productToken}:{numericToken}");
            return identities;
        }

        return identities;
    }

    private static string? FindPrecedingContext(IReadOnlyList<string> tokens, int numericIndex)
    {
        var examined = 0;
        for (var i = numericIndex - 1; i >= 0 && examined < 3; i--, examined++)
        {
            var token = tokens[i];
            if (token.Any(char.IsDigit))
                return null;
            if (GenericVersionMarkers.Contains(token))
                continue;
            return token;
        }
        return null;
    }

    private static string? FindFollowingProductToken(IReadOnlyList<string> tokens, int numericIndex)
    {
        var examined = 0;
        for (var i = numericIndex + 1; i < tokens.Count && examined < 3; i++, examined++)
        {
            var token = tokens[i];
            if (token.Any(char.IsDigit))
                return null;
            if (GenericVersionMarkers.Contains(token))
                continue;
            if (NarrativeNumberContexts.Contains(token) || IncidentalCalendarContexts.Contains(token))
                return null;
            return token;
        }
        return null;
    }

    private static bool HasStrongVersionMarkerBefore(IReadOnlyList<string> tokens, int numericIndex)
    {
        for (var i = Math.Max(0, numericIndex - 2); i < numericIndex; i++)
        {
            if (StrongVersionMarkers.Contains(tokens[i]))
                return true;
        }
        return false;
    }

    private static bool IsLikelyCalendarYear(string token) =>
        token.Length == 4
        && int.TryParse(token, out var year)
        && year is >= 1900 and <= 2100;

    private static bool IsIncidentalPeriodToken(string token) =>
        Regex.IsMatch(token, @"^(?:q[1-4]|h[12]|fy\d{2,4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static HashSet<string> Tokens(string value) =>
        OrderedTokens(value).ToHashSet(StringComparer.Ordinal);

    private static List<string> OrderedTokens(string value)
    {
        var normalized = LetterVersionSeparatorPattern().Replace(value.ToLowerInvariant(), " ");
        normalized = ConventionalVersionPrefixPattern().Replace(normalized, "");
        return TokenPattern().Matches(normalized).Cast<Match>()
            .Select(match => match.Value.Trim('.', '-', '_'))
            .Where(token => token.Length >= 2 || token.Any(char.IsDigit))
            .Where(token => !StopWords.Contains(token))
            .ToList();
    }

    private static double Similarity(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
            return 0;

        var intersection = left.Count(right.Contains);
        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static string HashSeed(string seed) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..24]
            .ToLowerInvariant();

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}+#._-]*", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"(?<=[\p{L}])[-_](?=\d)", RegexOptions.CultureInvariant)]
    private static partial Regex LetterVersionSeparatorPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])v(?=\d)", RegexOptions.CultureInvariant)]
    private static partial Regex ConventionalVersionPrefixPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])\d+(?:[.,]\d+)?\s*(?:%|percent(?:age)?(?:\s+points?)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PercentagePattern();

    [GeneratedRegex(@"[$€£¥]\s*\d+(?:[.,]\d+)*", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyNumberPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])\d+(?:\.\d+)?x(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MultiplierPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])\d+(?:[.,]\d+)?\s*(?:thousand|million|billion|trillion|users?|customers?|parameters?|tokens?|gb|tb|mb|fps|hz|watts?|days?|weeks?|months?|years?|hours?|minutes?|seconds?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuantityPattern();
}
