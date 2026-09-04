using System.Security.Cryptography;
using System.Text;

public static class SourceIdentity
{
    private const string Prefix = "source:";

    public static string ForUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "";
        if (IsIdentifier(url))
            return url.Trim().ToLowerInvariant();

        var digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim())))
            .ToLowerInvariant();
        return Prefix + digest[..24];
    }

    public static string NormalizePersisted(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return IsIdentifier(value) ? value.Trim().ToLowerInvariant() : ForUrl(value);
    }

    public static bool Matches(string? persistedIdentity, string? requestUrl)
    {
        if (string.IsNullOrWhiteSpace(persistedIdentity) || string.IsNullOrWhiteSpace(requestUrl))
            return false;
        return string.Equals(
            NormalizePersisted(persistedIdentity),
            ForUrl(requestUrl),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string PublicLabel(string? identityOrUrl)
    {
        var identity = NormalizePersisted(identityOrUrl);
        if (string.IsNullOrWhiteSpace(identity))
            return "source";
        var suffix = identity.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? identity[Prefix.Length..]
            : identity;
        return "source-" + suffix[..Math.Min(8, suffix.Length)];
    }

    public static NewsItem Sanitize(NewsItem article)
    {
        if (string.IsNullOrWhiteSpace(article.FeedUrl))
            return article;

        var identity = NormalizePersisted(article.FeedUrl);
        return article with
        {
            FeedUrl = identity,
            Source = PublicLabel(identity)
        };
    }

    public static NewsItem Rehydrate(NewsItem article, BriefingProfile profile)
    {
        if (string.IsNullOrWhiteSpace(article.FeedUrl))
            return article;

        var configured = profile.Sources.FirstOrDefault(source =>
            Matches(article.FeedUrl, source.Url));
        return configured is null
            ? article
            : article with
            {
                FeedUrl = ForUrl(configured.Url),
                Source = configured.Name
            };
    }

    public static string RedactFrom(string? text, string? requestUrl)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(requestUrl))
            return text ?? "";
        return text.Replace(requestUrl, "[redacted source]", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIdentifier(string value) =>
        value.Trim().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
}
