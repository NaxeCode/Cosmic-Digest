public static class EmailPreviewWriter
{
    public static int Write(string outputPath = "artifacts/email-preview.html")
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new BriefingProfile { DisplayName = "Aladdin", Version = "preview-internal" };
        var candidates = new List<ScoredArticle>
        {
            Candidate(1, "A platform release changes the recommended agent workflow", "OpenAI News", now, "act", 2),
            Candidate(2, "A database capability removes a reliability workaround", "PostgreSQL News", now.AddMinutes(-20), "watch", 1),
            Candidate(3, "A new systems mechanism is worth practicing once", ".NET Blog", now.AddMinutes(-40), "learn", 1),
            Candidate(4, "A lower-value candidate is intentionally suppressed", "Example", now.AddHours(-1), "watch", 1),
            Candidate(5, "Another lower-value candidate is intentionally suppressed", "Example", now.AddHours(-2), "watch", 1)
        };
        var briefing = new BriefingDocument
        {
            BottomLine = "One low-regret action is ready; two changes are worth keeping in view.",
            Items = new List<BriefingItem>
            {
                Item(1, "act", "The supported integration path changed.", "It removes repeated setup work in the current agent workflow.", "Review the migration note and update the next implementation."),
                Item(2, "watch", "A stable database feature reached general availability.", "It may simplify a reliability pattern, but no current system needs migration yet.", ""),
                Item(3, "learn", "The runtime exposed a mechanism that changes how backpressure is modeled.", "Understanding it improves backend design judgment beyond this release.", "Implement the smallest producer-consumer example and explain the failure mode.")
            }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, DigestComposer.BuildHtml(profile, candidates, briefing, now));
        Console.WriteLine($"Email preview written to {Path.GetFullPath(outputPath)}");
        return 0;
    }

    private static ScoredArticle Candidate(
        int index,
        string title,
        string source,
        DateTimeOffset published,
        string decision,
        int sourceCount) =>
        new(
            new NewsItem(title, $"https://example.com/preview/{index}", published, source),
            5 - (index * 0.1),
            new[] { "Preview" },
            $"preview-event-{index}",
            sourceCount,
            sourceCount > 1 ? new[] { source, "Independent source" } : new[] { source });

    private static BriefingItem Item(
        int index,
        string decision,
        string changed,
        string matters,
        string next) => new()
    {
        ArticleIndex = index,
        WhatChanged = changed,
        WhyItMatters = matters,
        Decision = decision,
        NextStep = next,
        Confidence = decision == "act" ? "high" : "medium"
    };
}
