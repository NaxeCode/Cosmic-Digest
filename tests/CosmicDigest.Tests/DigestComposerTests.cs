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
                    WhatChanged = "A <script> tag changed.\n# deceptive ![track](https://attacker.example/pixel)",
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
        Assert.DoesNotContain("\n# deceptive", markdown, StringComparison.Ordinal);
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

    [Fact]
    public void Html_uses_stella_identity_without_exposing_internal_profile_version()
    {
        var profile = Profile();
        var candidate = new ScoredArticle(
            new NewsItem("A useful release", "https://example.com/release", DateTimeOffset.Parse("2026-09-03T12:00:00Z"), "Official source"),
            5,
            new[] { "Backend" },
            "event-1",
            2,
            new[] { "Official source", "Second source" });
        var briefing = new BriefingDocument
        {
            BottomLine = "One practical action is ready.",
            Items = new List<BriefingItem>
            {
                new()
                {
                    ArticleIndex = 1,
                    WhatChanged = "The supported API changed.",
                    WhyItMatters = "It changes the implementation choice.",
                    Decision = "act",
                    NextStep = "Review the migration note.",
                    Confidence = "high"
                }
            }
        };

        var html = DigestComposer.BuildHtml(
            profile,
            new[] { candidate },
            briefing,
            DateTimeOffset.Parse("2026-09-03T12:00:00Z"),
            new EmailBrandOptions("Stella · Cosmic Digest", "https://example.com/stella.png"));

        Assert.Contains("Stella", html);
        Assert.Contains("Cosmic Digest", html);
        Assert.Contains("Reader&#39;s Intelligence Brief", html);
        Assert.Contains("1 ACT", html);
        Assert.Contains("corroborated by 2 sources", html);
        Assert.Contains("width=\"48\" height=\"48\"", html);
        Assert.DoesNotContain(profile.Version, html);
    }

    [Fact]
    public void Html_adds_signed_feedback_only_when_fully_configured()
    {
        var candidate = new ScoredArticle(
            new NewsItem("Release", "https://example.com/release", DateTimeOffset.Parse("2026-09-03T12:00:00Z"), "Example"),
            5,
            new[] { "Backend" },
            "event-1");
        var briefing = new BriefingDocument
        {
            BottomLine = "One update.",
            Items = new List<BriefingItem>
            {
                new() { ArticleIndex = 1, WhatChanged = "Changed.", WhyItMatters = "Useful.", Decision = "watch", Confidence = "high" }
            }
        };

        var disabled = DigestComposer.BuildHtml(Profile(), new[] { candidate }, briefing, DateTimeOffset.UtcNow,
            new EmailBrandOptions("Stella", "https://example.com/stella.png"));
        var enabled = DigestComposer.BuildHtml(Profile(), new[] { candidate }, briefing, DateTimeOffset.UtcNow,
            new EmailBrandOptions("Stella", "https://example.com/stella.png", "https://feedback.example.com/feedback", "secret"));

        Assert.DoesNotContain("Was this signal right?", disabled);
        Assert.Contains("Was this signal right?", enabled);
        Assert.Contains("token=", enabled);
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
