public sealed class DigestComposerTests
{
    [Fact]
    public void Composer_escapes_untrusted_content_and_tracks_only_displayed_articles()
    {
        var article = new NewsItem(
            "Release <script>alert(1)</script>",
            "https://example.com/release",
            DateTimeOffset.Parse("2026-09-03T12:00:00Z"),
            "Example");
        var candidates = new List<ScoredArticle>
        {
            new(article, 5, new[] { "Backend" })
        };
        var profile = new BriefingProfile
        {
            Version = "test-v1",
            DisplayName = "Reader",
            Feeds = new List<string> { "https://example.com/feed" },
            Priorities = new List<BriefingPriority>
            {
                new() { Name = "Backend", Signals = new List<string> { "release" } }
            }
        };
        var briefing = new BriefingDocument
        {
            BottomLine = "One <b>real</b> update.",
            Items = new List<BriefingItem>
            {
                new()
                {
                    ArticleIndex = 1,
                    WhatChanged = "A <script> tag changed.",
                    WhyItMatters = "It affects APIs.",
                    Decision = "watch",
                    Confidence = "high"
                }
            }
        };

        var markdown = DigestComposer.BuildMarkdown(
            profile,
            candidates,
            briefing,
            DateTimeOffset.Parse("2026-09-03T12:00:00Z"));

        Assert.DoesNotContain("<script>", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reader's Intelligence Brief", markdown);
        Assert.Single(DigestComposer.DisplayedArticles(candidates, briefing));
    }
}
