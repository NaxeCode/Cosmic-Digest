using System.Globalization;
using System.Net;

public static class PublisherIdentity
{
    private sealed record PublicSuffixRules(
        HashSet<string> Exact,
        HashSet<string> WildcardSuffixes,
        HashSet<string> Exceptions);

    private static readonly Lazy<PublicSuffixRules> Rules = new(LoadRules);

    public static string For(NewsItem article)
    {
        if (!Uri.TryCreate(article.Link, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return "source:" + article.Source.Trim().ToLowerInvariant();
        }
        return ForHost(uri.IdnHost);
    }

    public static string ForHost(string host)
    {
        host = host.Trim('.').ToLowerInvariant();
        if (host.Length == 0 || IPAddress.TryParse(host, out _))
            return host;

        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length <= 1)
            return host;

        var rules = Rules.Value;
        var exceptionLabels = 0;
        var publicSuffixLabels = 1;
        for (var i = 0; i < labels.Length; i++)
        {
            var candidate = string.Join(".", labels[i..]);
            var candidateLabels = labels.Length - i;
            if (rules.Exceptions.Contains(candidate))
                exceptionLabels = Math.Max(exceptionLabels, candidateLabels);
            if (rules.Exact.Contains(candidate))
                publicSuffixLabels = Math.Max(publicSuffixLabels, candidateLabels);
            if (i + 1 < labels.Length)
            {
                var wildcardSuffix = string.Join(".", labels[(i + 1)..]);
                if (rules.WildcardSuffixes.Contains(wildcardSuffix))
                    publicSuffixLabels = Math.Max(publicSuffixLabels, candidateLabels);
            }
        }

        if (exceptionLabels > 0)
            return string.Join(".", labels[^exceptionLabels..]);
        var registrableLabels = Math.Min(labels.Length, publicSuffixLabels + 1);
        return string.Join(".", labels[^registrableLabels..]);
    }

    private static PublicSuffixRules LoadRules()
    {
        var exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wildcards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exceptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(PublisherIdentity).Assembly;
        var resourceName = assembly.GetManifestResourceNames().SingleOrDefault(name =>
            name.EndsWith("public_suffix_list.dat", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The embedded Public Suffix List is missing.");
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The embedded Public Suffix List could not be opened.");
        using var reader = new StreamReader(stream);
        var idn = new IdnMapping();
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            var isException = line[0] == '!';
            var isWildcard = line.StartsWith("*.", StringComparison.Ordinal);
            var rule = isException ? line[1..] : isWildcard ? line[2..] : line;
            try
            {
                rule = idn.GetAscii(rule).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (isException)
                exceptions.Add(rule);
            else if (isWildcard)
                wildcards.Add(rule);
            else
                exact.Add(rule);
        }
        return new PublicSuffixRules(exact, wildcards, exceptions);
    }
}
