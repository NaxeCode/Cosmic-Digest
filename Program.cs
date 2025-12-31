// Program.cs
using DotNetEnv;
using System.Net.Http.Headers;
using System.Text.Json;
using Markdig;

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

// 4) Price tracking (optional)
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

// 6) Optional: Generate AI summary from actual articles
string? aiSummary = null;
var enableAi = Environment.GetEnvironmentVariable("ENABLE_AI_SUMMARY")?.ToLower() == "true";
if (enableAi && relevant.Any())
{
    try
    {
        var userProfile = $"Topics: {string.Join(", ", topics)}\nKeywords: {string.Join(", ", keywords)}";
        aiSummary = await NewsAi.SummarizeNewsAsync(userProfile, relevant);
        Console.WriteLine("✓ AI summary generated from top articles");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"AI summary failed: {ex.Message}");
    }
}

// 7) Generate daily coding challenge
string? dailyChallenge = null;
if (enableAi)
{
    try
    {
        dailyChallenge = await ChallengeGenerator.GenerateDailyChallengeAsync();
        if (!string.IsNullOrWhiteSpace(dailyChallenge))
            Console.WriteLine("✓ Daily challenge generated");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Challenge generation failed: {ex.Message}");
    }
}

// 8) Compose digest
var markdown = DigestComposer.BuildMarkdown(relevant, state.Prices, aiSummary, dailyChallenge);

// 9) Send via Resend
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

// Convert markdown to HTML with better styling
var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; color: #333; max-width: 800px; margin: 0 auto; padding: 20px; }}
        h1 {{ color: #2563eb; border-bottom: 3px solid #2563eb; padding-bottom: 10px; }}
        h2 {{ color: #1e40af; margin-top: 30px; border-bottom: 1px solid #e5e7eb; padding-bottom: 8px; }}
        h3 {{ color: #1e3a8a; }}
        a {{ color: #2563eb; text-decoration: none; }}
        a:hover {{ text-decoration: underline; }}
        ul {{ padding-left: 20px; }}
        li {{ margin: 8px 0; }}
        pre {{ background: #f3f4f6; padding: 15px; border-radius: 6px; overflow-x: auto; }}
        code {{ background: #f3f4f6; padding: 2px 6px; border-radius: 3px; font-family: 'Courier New', monospace; }}
        .footer {{ margin-top: 40px; padding-top: 20px; border-top: 1px solid #e5e7eb; color: #6b7280; font-size: 0.9em; }}
    </style>
</head>
<body>
{Markdown.ToHtml(markdown)}
<div class='footer'>
    <p>📧 Cosmic Digest • Powered by <a href='https://resend.com'>Resend</a> & <a href='https://openai.com'>OpenAI</a></p>
</div>
</body>
</html>";

var payload = JsonSerializer.Serialize(new
{
    from,
    to = new[] { to },
    subject = "Your Daily AI Digest",
    text = markdown,
    html = htmlBody
});

var response = await http.PostAsync("https://api.resend.com/emails",
    new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

var result = await response.Content.ReadAsStringAsync();

if (response.IsSuccessStatusCode)
    Console.WriteLine($"✓ Email sent successfully");
else
    Console.Error.WriteLine($"✗ Email failed: {response.StatusCode} - {result}");

// 10) Save state
state.LastDigestUtc = DateTimeOffset.UtcNow;
StateStore.Save(state);

return response.IsSuccessStatusCode ? 0 : 1;
