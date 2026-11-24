public sealed class SourceExcerptTests
{
    private static NewsItem Article(string? summary) =>
        new("Night camera release", "https://fixture.example/camera", DateTimeOffset.UnixEpoch, "Instrument Desk", summary);

    [Theory]
    [InlineData("The shutter  opens at midnight.", "The shutter opens at midnight.")]
    [InlineData("Night camera release", "Night camera release")]
    public void Accepts_visible_case_sensitive_text_from_either_field(string quote, string expected)
    {
        var article = Article("<p>The shutter\n<strong>opens</strong> at midnight.</p>");
        Assert.Equal(expected, SourceExcerpt.VerifyQuote(article, quote));
    }

    [Theory]
    [InlineData("not present.")]
    [InlineData("Not provided.")]
    [InlineData("night camera release")]
    [InlineData("camera release Extra")]
    [InlineData("release")]
    [InlineData(null)]
    public void Rejects_missing_wrong_case_placeholder_cross_field_and_short_quotes(string? quote)
    {
        Assert.Throws<InvalidOperationException>(() => SourceExcerpt.VerifyQuote(Article(quote == "camera release Extra" ? "Extra hours." : null), quote));
    }

    [Fact]
    public void Rejects_text_outside_the_model_window_or_beyond_the_quote_limit()
    {
        var article = Article(new string('a', 1_800) + "Invisible supporting evidence.");
        Assert.DoesNotContain("Invisible", SourceExcerpt.ForPrompt(article.Summary));
        Assert.Throws<InvalidOperationException>(() => SourceExcerpt.VerifyQuote(article, "Invisible supporting evidence."));
        Assert.Throws<InvalidOperationException>(() => SourceExcerpt.VerifyQuote(article, new string('a', 241)));
    }
}
