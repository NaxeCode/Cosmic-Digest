using System.Text.RegularExpressions;

// A research policy evaluated after the shipping validator accepts an answer.
internal static class CorrectionReview
{
    private static readonly Regex Identifiers = new(
        @"\b(?:[A-Z]{2,8}-[0-9]+|[0-9]+\.[0-9]+(?:\.[0-9]+)?)\b", RegexOptions.CultureInvariant);
    private static readonly Regex Notice = new(
        @"\b(cancel(?:led|ed)|hold|suspend(?:ed)?|withdraw(?:n|s)?|recall(?:ed)?|postpone(?:d)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string? Reason(IEnumerable<NewsItem> selected, IReadOnlyList<NewsItem> candidates)
    {
        foreach (var source in selected)
        {
            var keys = Identifiers.Matches(Text(source)).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
            if (keys.Count == 0) continue;
            foreach (var newer in candidates.Where(article => article.Published > source.Published))
            {
                var notice = Text(newer);
                if (!Notice.IsMatch(notice)) continue;
                var shared = Identifiers.Matches(notice).FirstOrDefault(match => keys.Contains(match.Value));
                if (shared is not null) return $"Later notice about {shared.Value}: {newer.Title}";
            }
        }
        return null;
    }

    private static string Text(NewsItem item) => item.Title + " " + item.Summary;
}
