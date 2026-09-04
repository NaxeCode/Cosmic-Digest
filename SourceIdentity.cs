using System.Security.Cryptography;
using System.Text;

public static class SourceIdentity
{
    private const string Prefix = "source:";
    private static readonly HashSet<string> DurableQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "article", "article_id", "entry", "id", "lang", "locale", "p", "page", "post", "post_id", "slug",
        "story", "story_id", "v", "version"
    };

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
        var identity = NormalizePersisted(article.FeedUrl);
        return article with
        {
            Link = SanitizeArticleLink(article.Link),
            FeedUrl = string.IsNullOrWhiteSpace(identity)
                ? article.FeedUrl
                : identity,
            Source = string.IsNullOrWhiteSpace(identity)
                ? article.Source
                : PublicLabel(identity)
        };
    }

    public static NewsItem PrepareForProtectedStorage(NewsItem article)
    {
        var sanitized = Sanitize(article);
        return sanitized with
        {
            Link = PreserveFunctionalArticleLink(article.Link)
        };
    }

    public static string PreserveFunctionalArticleLink(string? link)
    {
        var trimmed = link?.Trim() ?? "";
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            return "";
        }

        return trimmed;
    }

    public static string SanitizeArticleLink(string? link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return "";
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = "",
            UserName = "",
            Password = "",
            Host = uri.Host.ToLowerInvariant()
        };
        builder.Query = string.Join('&', builder.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(IsDurableQueryParameter));
        if (builder.Path.Length > 1)
            builder.Path = builder.Path.TrimEnd('/');
        return builder.Uri.AbsoluteUri.TrimEnd('/');
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

    private static bool IsDurableQueryParameter(string parameter)
    {
        var parts = parameter.Split('=', 2);
        string key;
        string value;
        try
        {
            key = Uri.UnescapeDataString(parts[0]).Trim().ToLowerInvariant();
            value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]).Trim() : "";
        }
        catch (UriFormatException)
        {
            return false;
        }

        return DurableQueryKeys.Contains(key)
            && value.Length <= 160
            && !value.Contains('@')
            && !Uri.TryCreate(value, UriKind.Absolute, out _);
    }
}
