// Match only the representative text that is visible to the model. Feed markup and
// whitespace are normalized before prompting. Synthetic display placeholders are excluded.
public static class SourceExcerpt
{
    private const int PromptCharacterLimit = 1_800;

    public static string ForPrompt(string? raw)
    {
        var excerpt = Visible(raw);
        return excerpt.Length == 0 ? "Not provided." : excerpt;
    }

    public static string VerifyQuote(NewsItem article, string? quote)
    {
        var normalized = NormalizeWhitespace(quote);
        if (normalized.Length is < 12 or > 240)
            throw new InvalidOperationException("The evidence quote must contain 12 to 240 characters.");

        if (!Visible(article.Title).Contains(normalized, StringComparison.Ordinal)
            && !Visible(article.Summary).Contains(normalized, StringComparison.Ordinal))
            throw new InvalidOperationException("The evidence quote does not occur in the cited article's visible title or summary.");

        return normalized;
    }

    private static string Visible(string? raw)
    {
        var text = NormalizeWhitespace(ArticleText.ToPlainText(raw));
        if (text.Length <= PromptCharacterLimit)
            return text;

        var length = char.IsHighSurrogate(text[PromptCharacterLimit - 1])
            ? PromptCharacterLimit - 1 : PromptCharacterLimit;
        return text[..length];
    }

    private static string NormalizeWhitespace(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
