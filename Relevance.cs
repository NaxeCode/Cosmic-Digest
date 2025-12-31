// Relevance.cs
using System.Text.RegularExpressions;

public static class Relevance
{
    public static double Score(NewsItem n, HashSet<string> topics, HashSet<string> regions, HashSet<string> keywords)
    {
        // naive: title + summary keyword hits
        var text = $"{n.Title} {n.Summary}".ToLowerInvariant();
        double score = 0;

        foreach (var k in keywords) if (!string.IsNullOrWhiteSpace(k) && text.Contains(k.ToLowerInvariant())) score += 1.0;
        foreach (var t in topics) if (text.Contains(t.ToLowerInvariant())) score += 0.6;
        foreach (var r in regions) if (text.Contains(r.ToLowerInvariant())) score += 0.4;

        // recency boost (0..1 over 3 days)
        var ageHours = (DateTimeOffset.UtcNow - n.Published).TotalHours;
        var recency = Math.Max(0, 1 - (ageHours / 72.0));
        score += recency * 0.5;

        return score; // unbounded-ish; good enough for ranking
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common words to exclude from trends
        "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by", "from",
        "as", "is", "was", "are", "were", "be", "been", "being", "have", "has", "had", "do", "does", "did",
        "will", "would", "could", "should", "may", "might", "can", "this", "that", "these", "those", "it",
        "its", "they", "them", "their", "what", "which", "who", "when", "where", "why", "how", "all", "each",
        "every", "both", "few", "more", "most", "some", "such", "no", "nor", "not", "only", "own", "same",
        "so", "than", "too", "very", "just", "after", "before", "over", "under", "again", "further", "then",
        "once", "here", "there", "about", "into", "through", "during", "out", "up", "down", "off", "above",
        "below", "says", "said", "year", "years", "day", "days", "week", "weeks", "month", "months", "new"
    };

    public static List<TopicTrend> Trends(IReadOnlyList<NewsItem> all, int windowDays = 3, int prevDays = 3)
    {
        // group by rough topic from title nouns/keywords; for MVP use simple tokens
        var nowCut = DateTimeOffset.UtcNow.AddDays(-windowDays);
        var prevCut = DateTimeOffset.UtcNow.AddDays(-(windowDays + prevDays));

        var now = all.Where(n => n.Published >= nowCut).ToList();
        var prev = all.Where(n => n.Published < nowCut && n.Published >= prevCut).ToList();

        // naive topics: top unigrams bigrams from titles
        static IEnumerable<string> Tokens(string s)
        {
            s = Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9\s\-]", " ");
            var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                yield return words[i];
                if (i + 1 < words.Length) yield return $"{words[i]} {words[i + 1]}";
            }
        }

        var nowCounts = now.SelectMany(n => Tokens(n.Title)).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        var prevCounts = prev.SelectMany(n => Tokens(n.Title)).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());

        var candidates = nowCounts
            .Where(kv => kv.Value >= 3
                && kv.Key.Length > 3
                && !StopWords.Contains(kv.Key)
                && !kv.Key.All(char.IsDigit) // filter pure numbers like "2025"
                && !kv.Key.Contains("2025") // filter any phrase with year
                && !kv.Key.Contains("2024")
                && !kv.Key.StartsWith("in ")
                && !kv.Key.StartsWith("of ")
                && !kv.Key.StartsWith("the ")
                && kv.Key.Split(' ').Length <= 3) // limit to 3-word phrases max
            .OrderByDescending(kv => kv.Value)
            .Take(50);

        var trends = new List<TopicTrend>();
        foreach (var kv in candidates)
        {
            prevCounts.TryGetValue(kv.Key, out var p);
            var slope = kv.Value - p; // simplest “delta”
            trends.Add(new TopicTrend(kv.Key, kv.Value, p, slope, 0));
        }
        return trends.OrderByDescending(t => t.Slope).Take(12).ToList();
    }
}
