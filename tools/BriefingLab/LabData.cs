using System.Text.Json;

internal sealed record Corpus(DateTimeOffset Clock, BriefingProfile Profile, SourceReport[] Reports, RankExpectation[] ExpectedRanks);
internal sealed record RankExpectation(string SourceId, int Publishers);
internal sealed record SourceReport(string Id, string Kind, string Title, string Source, DateTimeOffset Published, string Link, string Summary)
{
    public NewsItem ToArticle() => new(Title, Link, Published, Source, Summary);
}

internal sealed record CaseFixture(string Id, string Title, string Note, bool KnownLimit,
    string Expected, string? ExpectedError, string[] ExpectedVisible, JsonElement Response);
internal sealed record Candidate(int Index, string SourceId, double Score, int Publishers, IReadOnlyList<string> Coverage);
internal sealed record CaseResult(CaseFixture Fixture, string Structure, string Actual, string? Error, BriefingDocument Displayed, string[] Visible, bool Matches);
