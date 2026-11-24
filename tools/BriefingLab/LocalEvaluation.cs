using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class LocalEvaluation
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly DateTimeOffset Clock = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
    private static readonly BriefingProfile Profile = new()
    {
        MaxItems = 1, MinimumScore = 0,
        Priorities = [new() { Name = "Observatory operations", Weight = 4, Signals = ["observatory"] }]
    };

    internal sealed record Gold(string? Id, string[] Requires, string[] Forbids, string Reason);
    private sealed record Packet(string Id, string Question, string[][] Reports, Gold Gold);
    private sealed record Trial(Packet Packet, string Order, ScoredArticle[] Candidates, string[] Ids,
        string System, string User, string PromptHash);
    private sealed record Completion(string Packet, string Order, string PromptHash, string Raw,
        int PromptTokens, int GeneratedTokens, string FinishReason, double Seconds);
    private sealed record Run(JsonElement Metadata, Completion[] Outputs);
    private sealed record Outcome(string Packet, string Order, string Question, Gold Gold, string[] Ids,
        NewsItem[] Sources, Completion Inference, bool Parsed, bool Structure, bool QuoteGate, bool HasItem,
        string? Error, string[] Selected, bool[] ItemQuotes, bool Rubric, string[] Failures);
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static IEnumerable<Trial> Trials()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "model-eval", "packets.json");
        var packets = JsonSerializer.Deserialize<Packet[]>(File.ReadAllText(path), Json)!;
        foreach (var packet in packets)
        {
            var articles = packet.Reports.Select(row => new NewsItem(row[2],
                $"https://{row[0]}.example/{packet.Id}", DateTimeOffset.Parse($"2026-11-01T{row[1]}:00Z"),
                $"Meridian {row[0]} desk", row[3])).ToList();
            var ranked = ArticleSelector.Rank(articles, Profile, [], Clock).ToArray();
            if (ranked.Length != articles.Count) throw new InvalidDataException($"Incomplete {packet.Id} rank.");
            foreach (var order in new[] { "ranked", "reversed" })
            {
                var candidates = order == "ranked" ? ranked : ranked.Reverse().ToArray();
                var ids = candidates.Select(c => new Uri(c.Article.Link).Host.Split('.')[0]).ToArray();
                var system = NewsAi.BuildSystemPrompt(Profile);
                var user = NewsAi.BuildCandidatePrompt(candidates) +
                    "\nReturn the JSON object only. Do not include Markdown or commentary.\nJSON schema:\n" +
                    Encoding.UTF8.GetString(NewsAi.BuildSchema(Profile.MaxItems));
                yield return new(packet, order, candidates, ids, system, user, Hash(system + "\n" + user));
            }
        }
    }

    internal static int Prepare(string output)
    {
        var prompts = Trials().Select(t => new { packet = t.Packet.Id, t.Order, t.Ids, t.System, t.User, t.PromptHash });
        File.WriteAllText(output, JsonSerializer.Serialize(prompts, Json) + "\n");
        return 0;
    }

    internal static int Score(string raw, string output)
    {
        var measured = SerializeReport(raw);
        File.WriteAllText(output, measured + "\n");
        Console.WriteLine(JsonDocument.Parse(measured).RootElement.GetProperty("metrics"));
        return 0;
    }

    internal static string SerializeReport(string raw)
    {
        var run = JsonSerializer.Deserialize<Run>(File.ReadAllText(raw), Json)!;
        var trials = Trials().ToArray();
        if (run.Outputs.Length != trials.Length) throw new InvalidDataException("Expected every prompt exactly once.");
        var completions = run.Outputs.ToDictionary(c => (c.Packet, c.Order));
        var outcomes = new List<Outcome>();
        foreach (var trial in trials)
        {
            var completion = completions[(trial.Packet.Id, trial.Order)];
            if (trial.PromptHash != completion.PromptHash) throw new InvalidDataException("Prompt mismatch.");
            outcomes.Add(Measure(trial, completion));
        }
        var pairs = outcomes.GroupBy(o => o.Packet).ToArray();
        static bool SameSelection(IGrouping<string, Outcome> pair) =>
            pair.All(o => o.Parsed && !o.Selected.Contains("invalid index"))
            && pair.First().Selected.Order(StringComparer.Ordinal).SequenceEqual(pair.Last().Selected.Order(StringComparer.Ordinal));
        var admitted = pairs.Where(pair => SameSelection(pair) && pair.All(o => o.QuoteGate)).ToArray();
        var metrics = new {
            trials = outcomes.Count, pairs = pairs.Length, parsed = outcomes.Count(o => o.Parsed),
            structure = outcomes.Count(o => o.Structure), quoteGate = outcomes.Count(o => o.QuoteGate),
            nonemptyQuoteGate = outcomes.Count(o => o.QuoteGate && o.HasItem),
            emptyQuoteGate = outcomes.Count(o => o.QuoteGate && !o.HasItem),
            attemptedItemQuotes = outcomes.Sum(o => o.ItemQuotes.Length),
            exactItemQuotes = outcomes.Sum(o => o.ItemQuotes.Count(verified => verified)),
            rubric = outcomes.Count(o => o.Rubric), both = outcomes.Count(o => o.QuoteGate && o.Rubric),
            quotePassRubricMiss = outcomes.Count(o => o.QuoteGate && !o.Rubric),
            orderAgreement = pairs.Count(SameSelection),
            retrospectiveAdmitted = admitted.Length, retrospectiveReferred = pairs.Length - admitted.Length,
            admittedAndRubricBothWays = admitted.Count(pair => pair.All(o => o.Rubric)),
        };
        return JsonSerializer.Serialize(new { run.Metadata, metrics, outcomes }, Json);
    }

    private static Outcome Measure(Trial trial, Completion output)
    {
        BriefingDocument? doc = null;
        var parsed = false;
        var structure = false;
        var quoteGate = false;
        string? error = null;
        try
        {
            doc = JsonSerializer.Deserialize<BriefingDocument>(output.Raw, Json) ?? throw new JsonException("Null document.");
            parsed = true;
            NewsAi.ValidateBriefingStructure(Profile, trial.Candidates, doc);
            structure = true;
            NewsAi.ValidateBriefing(Profile, trial.Candidates, doc);
            quoteGate = true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NullReferenceException)
        {
            error = ex.Message;
        }

        var items = doc?.Items ?? [];
        var selected = items.Select(item => item is not null && item.ArticleIndex >= 1 && item.ArticleIndex <= trial.Ids.Length
            ? trial.Ids[item.ArticleIndex - 1] : "invalid index").ToArray();
        // A separate item-level diagnostic ignores document cardinality and asks only
        // whether an individual quote belongs to the representative source it cites.
        var itemQuotes = items.Select(item =>
        {
            if (item is null || item.ArticleIndex < 1 || item.ArticleIndex > trial.Candidates.Length) return false;
            try
            {
                SourceExcerpt.VerifyQuote(trial.Candidates[item.ArticleIndex - 1].Article, item.EvidenceQuote);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }).ToArray();
        var gold = trial.Packet.Gold;
        var failures = new List<string>();
        if (!parsed) failures.Add("Response did not parse");
        if (gold.Id == null && selected.Length != 0) failures.Add("Expected abstention");
        if (gold.Id != null && !selected.SequenceEqual([gold.Id])) failures.Add("Expected source " + gold.Id);
        var prose = (doc?.BottomLine ?? "") + " " + string.Join(" ", items.Select(item => item?.WhatChanged ?? ""));
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        foreach (var pattern in gold.Requires)
            if (!Regex.IsMatch(prose, pattern, options)) failures.Add("Missing scope pattern: " + pattern);
        foreach (var pattern in gold.Forbids)
            if (Regex.IsMatch(prose, pattern, options)) failures.Add("Contradictory scope pattern: " + pattern);
        return new(trial.Packet.Id, trial.Order, trial.Packet.Question, gold, trial.Ids,
            trial.Candidates.Select(c => c.Article).ToArray(), output, parsed, structure, quoteGate, items.Count > 0,
            error, selected, itemQuotes, failures.Count == 0, failures.ToArray());
    }
}
