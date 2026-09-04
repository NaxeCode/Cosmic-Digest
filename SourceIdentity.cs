using System.Security.Cryptography;
using System.Text;

public static class SourceIdentity
{
    private const string Prefix = "source:";
    private static readonly HashSet<string> SensitiveQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "access_token", "accesstoken", "api_key", "apikey", "auth", "authorization", "code",
        "credential", "jwt", "key", "password", "pwd", "refresh_token", "refreshtoken", "secret",
        "session", "session_id", "sessionid", "sig", "signature", "subscriber", "token"
    };
    private static readonly HashSet<string> SensitiveQueryWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "auth", "credential", "jwt", "key", "password", "secret", "session", "sig", "signature", "token"
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
            .Where(pair => !IsPrivateOrTrackingQueryKey(pair.Split('=', 2)[0])));
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

    private static bool IsPrivateOrTrackingQueryKey(string encodedKey)
    {
        string key;
        try
        {
            key = Uri.UnescapeDataString(encodedKey).Trim().ToLowerInvariant();
        }
        catch (UriFormatException)
        {
            return true;
        }

        if (SensitiveQueryKeys.Contains(key))
            return true;
        if (key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
            || key is "fbclid" or "gclid" or "mc_cid" or "mc_eid" or "ref" or "ref_src")
        {
            return true;
        }
        return key.Split(new[] { '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(SensitiveQueryWords.Contains);
    }
}
