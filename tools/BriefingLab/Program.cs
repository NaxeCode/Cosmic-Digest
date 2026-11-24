using System.Globalization;
using System.Text.Json;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/BriefingLab -- OUTPUT_DIRECTORY");
    return 2;
}

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var assets = AppContext.BaseDirectory;
var corpus = Read<Corpus>("corpus.json");
var fixtures = Read<CaseFixture[]>("cases.json");
var ranked = ArticleSelector.Rank(corpus.Reports.Select(report => report.ToArticle()), corpus.Profile, [], corpus.Clock);
var byLink = corpus.Reports.ToDictionary(report => report.Link);
var candidates = ranked.Select((candidate, i) => new Candidate(i + 1, byLink[candidate.Article.Link].Id,
    candidate.Score, candidate.SourceCount, candidate.EvidenceSources)).ToArray();
var rankMatches = candidates.Select(c => new RankExpectation(c.SourceId, c.Publishers)).SequenceEqual(corpus.ExpectedRanks);

var results = new List<CaseResult>();
foreach (var fixture in fixtures)
{
    var response = fixture.Response.Deserialize<BriefingDocument>(json)
        ?? throw new InvalidDataException($"Missing response for {fixture.Id}.");
    var structure = "Accepted";
    try
    {
        NewsAi.ValidateBriefingStructure(corpus.Profile, ranked, fixture.Response.Deserialize<BriefingDocument>(json)!);
    }
    catch (InvalidOperationException) { structure = "Rejected"; }

    string? error = null;
    BriefingDocument displayed;
    string actual;
    try
    {
        displayed = NewsAi.ValidateBriefing(corpus.Profile, ranked, response);
        actual = displayed.Items.Count == 0 ? "No items" : "Accepted";
    }
    catch (InvalidOperationException ex)
    {
        error = ex.Message;
        actual = "Fallback";
        displayed = NewsAi.BuildDeterministicFallback(corpus.Profile, ranked);
    }

    var visible = DigestComposer.DisplayedCandidates(ranked, displayed)
        .Select(candidate => byLink[candidate.Article.Link].Id).ToArray();
    var errorMatches = fixture.ExpectedError is null
        ? error is null : error?.Contains(fixture.ExpectedError, StringComparison.Ordinal) == true;
    var matches = actual == fixture.Expected && errorMatches && visible.SequenceEqual(fixture.ExpectedVisible);
    results.Add(new(fixture, structure, actual, error, displayed, visible, matches));
    Console.WriteLine($"{(matches ? "MATCH" : "MISMATCH"),-8} {fixture.Id,-22} expected {fixture.Expected,-9} actual {actual,-9} [{string.Join(", ", visible)}]");
}

var report = new
{
    corpus.Clock, corpus.Reports, corpus.ExpectedRanks, candidates, rankMatches, cases = results,
    matched = results.Count(result => result.Matches),
    additionalRejections = results.Count(result => result.Structure == "Accepted" && result.Actual == "Fallback"),
    unsupportedAccepted = results.Count(result => result.Fixture.KnownLimit && result.Actual == "Accepted")
};
var serialized = JsonSerializer.Serialize(report, json);
// System.Text.Json's default encoder escapes '<' in feed and fixture text before embedding in HTML.
var web = Path.Combine(assets, "web");
var page = File.ReadAllText(Path.Combine(web, "index.html"))
    .Replace("/* LAB_STYLE */", File.ReadAllText(Path.Combine(web, "lab.css")), StringComparison.Ordinal)
    .Replace("/* LAB_SCRIPT */", File.ReadAllText(Path.Combine(web, "lab.js")), StringComparison.Ordinal)
    .Replace("<!-- LAB_REPORT -->", serialized, StringComparison.Ordinal);

var output = Directory.CreateDirectory(args[0]).FullName;
File.WriteAllText(Path.Combine(output, "report.json"), serialized + "\n");
File.WriteAllText(Path.Combine(output, "index.html"), page);
Console.WriteLine($"Ranked {candidates.Length} candidates from {corpus.Reports.Length} reports. Rank order and publisher counts match: {rankMatches}.");
Console.WriteLine($"{report.matched}/{results.Count} case expectations matched. Quote gate added {report.additionalRejections} rejections. Unsupported accepted: {report.unsupportedAccepted}.");
return rankMatches && results.All(result => result.Matches) ? 0 : 1;

T Read<T>(string name) =>
    JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(assets, "fixtures", name)), json)
    ?? throw new InvalidDataException($"Empty fixture: {name}");
