public sealed class NewsAiTests
{
    [Fact]
    public void ValidateBriefing_rejects_invalid_article_references()
    {
        var briefing = ValidBriefing();
        briefing.Items[0].ArticleIndex = 2;

        var error = Assert.Throws<InvalidOperationException>(() =>
            NewsAi.ValidateBriefing(Profile(), Candidates(), briefing));

        Assert.Contains("invalid article index", error.Message);
    }

    [Fact]
    public void ValidateBriefing_rejects_blank_required_analysis()
    {
        var briefing = ValidBriefing();
        briefing.Items[0].WhyItMatters = "   ";

        var error = Assert.Throws<InvalidOperationException>(() =>
            NewsAi.ValidateBriefing(Profile(), Candidates(), briefing));

        Assert.Contains("incomplete analysis", error.Message);
    }

    [Fact]
    public void ValidateBriefing_rejects_actions_without_a_next_step()
    {
        var briefing = ValidBriefing();
        briefing.Items[0].Decision = "act";
        briefing.Items[0].NextStep = "";

        var error = Assert.Throws<InvalidOperationException>(() =>
            NewsAi.ValidateBriefing(Profile(), Candidates(), briefing));

        Assert.Contains("action without a next step", error.Message);
    }

    private static BriefingProfile Profile() => new() { MaxItems = 5 };

    private static IReadOnlyList<ScoredArticle> Candidates() => new[]
    {
        new ScoredArticle(
            new NewsItem("Release", "https://example.com/release", DateTimeOffset.UtcNow, "Example"),
            5,
            new[] { "Backend" })
    };

    private static BriefingDocument ValidBriefing() => new()
    {
        BottomLine = "One material update.",
        Items = new List<BriefingItem>
        {
            new()
            {
                ArticleIndex = 1,
                WhatChanged = "A supported change.",
                WhyItMatters = "It affects the current implementation.",
                Decision = "watch",
                NextStep = "",
                Confidence = "high"
            }
        }
    };
}
