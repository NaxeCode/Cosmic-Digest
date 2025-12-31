// Program.cs
using DotNetEnv;
using System.Net.Http.Headers;
using System.Text.Json;

// Load .env locally
Env.Load(".env");

// Helper to parse comma-separated env vars
static HashSet<string> ParseEnv(string key) =>
    (Environment.GetEnvironmentVariable(key) ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

// 1) Load state
var state = StateStore.Load();

// 2) Ingest RSS feeds
var feeds = ParseEnv("RSS_FEEDS").ToArray();
var freshNews = await RssIngestor.FetchAsync(feeds);
StateStore.AppendNews(state, freshNews, keepDays: 10);

// 3) Score relevance
var topics = ParseEnv("PREF_TOPICS");
var regions = ParseEnv("PREF_REGIONS");
var keywords = ParseEnv("PREF_KEYWORDS");

var scored = state.CacheNews
    .Select(n => (n, s: Relevance.Score(n, topics, regions, keywords)))
    .OrderByDescending(x => x.s)
    .ToList();

var relevant = scored.Where(x => x.s > 0.75).Select(x => x.n).Take(30).ToList();

// 4) Developing stories (last 3 vs previous 3 days)
var trends = Relevance.Trends(state.CacheNews, 3, 3);

// 5) Price tracking (optional)
var watchlist = (Environment.GetEnvironmentVariable("PRICE_WATCH") ?? "")
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(entry =>
    {
        var parts = entry.Split('|', StringSplitOptions.TrimEntries);
        return (
            Name: parts.ElementAtOrDefault(0) ?? "Item",
            Url: parts.ElementAtOrDefault(1) ?? "",
            Cur: parts.ElementAtOrDefault(2) ?? "USD"
        );
    })
    .Where(w => !string.IsNullOrWhiteSpace(w.Url))
    .ToList();

if (watchlist.Any())
{
    var updated = await PriceTracker.UpdateAsync(state, watchlist, new NaivePriceFetcher());
    foreach (var item in updated) StateStore.UpsertPrice(state, item);
}

// 6) Optional: Generate AI summary
string? aiSummary = null;
var enableAi = Environment.GetEnvironmentVariable("ENABLE_AI_SUMMARY")?.ToLower() == "true";
if (enableAi)
{
    try
    {
        var userProfile = $"Topics: {string.Join(", ", topics)}\nRegions: {string.Join(", ", regions)}\nKeywords: {string.Join(", ", keywords)}";
        aiSummary = await NewsAi.SummarizeWorldNewsAsync(userProfile);
        Console.WriteLine("✓ AI summary generated");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"AI summary failed: {ex.Message}");
    }
}

// 7) Compose digest
var markdown = DigestComposer.BuildMarkdown(trends, relevant, state.Prices, aiSummary);

// 8) Send via Resend
var apiKey = Environment.GetEnvironmentVariable("RESEND_API_KEY");
var to = Environment.GetEnvironmentVariable("MAIL_TO");
var from = Environment.GetEnvironmentVariable("MAIL_FROM") ?? "digest@resend.dev";

if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(to))
{
    Console.Error.WriteLine("Missing RESEND_API_KEY or MAIL_TO");
    return 1;
}

using var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

var payload = JsonSerializer.Serialize(new
{
    from,
    to = new[] { to },
    subject = "Your Daily AI Digest",
    text = markdown
});

var response = await http.PostAsync("https://api.resend.com/emails",
    new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

var result = await response.Content.ReadAsStringAsync();

if (response.IsSuccessStatusCode)
    Console.WriteLine($"✓ Email sent successfully");
else
    Console.Error.WriteLine($"✗ Email failed: {response.StatusCode} - {result}");

// 9) Save state
state.LastDigestUtc = DateTimeOffset.UtcNow;
StateStore.Save(state);

return response.IsSuccessStatusCode ? 0 : 1;
