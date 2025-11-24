// Program.cs
using DotNetEnv;
using System.Net.Http.Headers;
using System.Text;

// Load .env locally; use Fly secrets in prod
Env.Load(".env"); // safe if not present

// 1) Load state
var state = StateStore.Load();

// 2) Ingest RSS
var feeds = (Environment.GetEnvironmentVariable("RSS_FEEDS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var freshNews = await RssIngestor.FetchAsync(feeds);
StateStore.AppendNews(state, freshNews, keepDays: 10);

// 3) Score relevance
HashSet<string> topics = (Environment.GetEnvironmentVariable("PREF_TOPICS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
HashSet<string> regions = (Environment.GetEnvironmentVariable("PREF_REGIONS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
HashSet<string> keywords = (Environment.GetEnvironmentVariable("PREF_KEYWORDS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

var scored = state.CacheNews
    .Select(n => (n, s: Relevance.Score(n, topics, regions, keywords)))
    .OrderByDescending(x => x.s)
    .ToList();

var relevant = scored.Where(x => x.s > 0.75).Select(x => x.n).Take(30).ToList();

// 4) Developing stories (last 3 vs previous 3 days)
var trends = Relevance.Trends(state.CacheNews, 3, 3);

// 5) Price update
var watch = (Environment.GetEnvironmentVariable("PRICE_WATCH") ?? "")
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(s =>
    {
        var parts = s.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var name = parts.ElementAtOrDefault(0) ?? "Item";
        var url = parts.ElementAtOrDefault(1) ?? "";
        var cur = parts.ElementAtOrDefault(2) ?? "USD";
        return (Name: name, Url: url, Cur: cur);
    });

var priceFetcher = new NaivePriceFetcher(); // swap for API-backed later
var updated = await PriceTracker.UpdateAsync(state, watch, priceFetcher);
foreach (var item in updated) StateStore.UpsertPrice(state, item);

// 6) Compose body
var markdown = DigestComposer.BuildMarkdown(trends, relevant, state.Prices);

// 7) Send via Mailgun (HTTP)
var apiKey = Environment.GetEnvironmentVariable("MAILGUN_API_KEY");
var domain = Environment.GetEnvironmentVariable("MAILGUN_DOMAIN");
var to = Environment.GetEnvironmentVariable("MAIL_TO") ?? "a@example.com";
var from = Environment.GetEnvironmentVariable("MAIL_FROM") ?? $"bot@{domain}";
if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(domain))
{
    Console.Error.WriteLine("Missing Mailgun config");
    return 1;
}

using var http = new HttpClient();
http.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{apiKey}")));
var content = new FormUrlEncodedContent(new[]
{
    new KeyValuePair<string,string>("from", from),
    new KeyValuePair<string,string>("to",   to),
    new KeyValuePair<string,string>("subject", "Your 3-Day AI Digest"),
    new KeyValuePair<string,string>("text", markdown)
});
var resp = await http.PostAsync($"https://api.mailgun.net/v3/{domain}/messages", content);
Console.WriteLine($"{(int)resp.StatusCode} {resp.ReasonPhrase}");
Console.WriteLine(await resp.Content.ReadAsStringAsync());

// 8) Save state + update last digest timestamp if >=72h gate passes
state.LastDigestUtc = DateTimeOffset.UtcNow;
StateStore.Save(state);

return resp.IsSuccessStatusCode ? 0 : 1;
