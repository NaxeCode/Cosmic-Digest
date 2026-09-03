using Markdig;

public sealed class DigestComposerTests
{
    [Fact]
    public void Composer_escapes_untrusted_content_and_tracks_only_displayed_articles()
    {
        var article = new NewsItem(
            "Release <script>alert(1)</script>",
            "https://example.com/release",
            DateTimeOffset.Parse("2026-09-03T12:00:00Z"),
            "![source](https://attacker.example/source-pixel)");
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
                    WhatChanged = "A <script> tag changed. ![track](https://attacker.example/pixel)",
                    WhyItMatters = "See [fake evidence](https://attacker.example/deceptive).",
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
        var rendered = Markdown.ToHtml(markdown, new MarkdownPipelineBuilder().DisableHtml().Build());
        Assert.DoesNotContain("<img", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"https://attacker.example", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("One &lt;b&gt;real&lt;/b&gt; update.", markdown);
        Assert.Contains("Reader's Intelligence Brief", markdown);
        Assert.Single(DigestComposer.DisplayedArticles(candidates, briefing));
    }

    [Fact]
    public void Composer_treats_blank_timezone_as_new_york()
    {
        var previousTimezone = Environment.GetEnvironmentVariable("TIMEZONE");
        try
        {
            Environment.SetEnvironmentVariable("TIMEZONE", "");
            var markdown = DigestComposer.BuildMarkdown(
                Profile(),
                Array.Empty<ScoredArticle>(),
                new BriefingDocument { BottomLine = "No update." },
                DateTimeOffset.Parse("2026-09-03T02:00:00Z"));

            Assert.Contains("Wednesday, September 2, 2026", markdown);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIMEZONE", previousTimezone);
        }
    }

    private static BriefingProfile Profile() => new()
    {
        Version = "test-v1",
        DisplayName = "Reader",
        Feeds = new List<string> { "https://example.com/feed" },
        Priorities = new List<BriefingPriority>
        {
            new() { Name = "Backend", Signals = new List<string> { "release" } }
        }
    };
}
