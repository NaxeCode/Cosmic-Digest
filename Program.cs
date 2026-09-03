using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DotNetEnv;
using Markdig;

Env.Load(".env");

var now = DateTimeOffset.UtcNow;
var profile = BriefingProfileLoader.Load();
var state = StateStore.Load();

Console.WriteLine($"Profile: {profile.Version}; priorities: {profile.Priorities.Count}; feeds: {profile.Feeds.Count}");

var freshNews = await RssIngestor.FetchAsync(profile.Feeds);
var keepDays = Math.Max(4, (int)Math.Ceiling(profile.LookbackHours / 24d) + 1);
StateStore.AppendNews(state, freshNews, keepDays);

var lookbackCutoff = now.AddHours(-profile.LookbackHours);
var migrationCutoff = state.ReviewedArticles.Count == 0 && state.LastDigestUtc is not null
    ? (state.LastDigestUtc.Value.AddHours(-3) > lookbackCutoff
        ? state.LastDigestUtc.Value.AddHours(-3)
        : lookbackCutoff)
    : lookbackCutoff;
var candidates = ArticleSelector.Rank(
    state.CacheNews,
    profile,
    state.ReviewedArticles.Select(item => item.Link),
    now,
    migrationCutoff);

Console.WriteLine($"Candidates above threshold: {candidates.Count}");

if (candidates.Count == 0)
{
    state.LastRunUtc = now;
    StateStore.Save(state);
    Console.WriteLine("No material new developments; email suppressed.");
    return 0;
}

BriefingDocument briefing;
var enableAi = Environment.GetEnvironmentVariable("ENABLE_AI_SUMMARY")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
if (enableAi)
{
    try
    {
        briefing = await NewsAi.BuildBriefingAsync(profile, candidates);
        Console.WriteLine($"AI selected {briefing.Items.Count} material item(s).");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"AI briefing failed; using deterministic fallback: {ex.Message}");
        briefing = NewsAi.BuildDeterministicFallback(profile, candidates);
    }
}
else
{
    briefing = NewsAi.BuildDeterministicFallback(profile, candidates);
}

var displayed = DigestComposer.DisplayedArticles(candidates, briefing);
if (displayed.Count == 0)
{
    StateStore.MarkReviewed(state, candidates.Select(item => item.Article), displayed, now);
    state.LastRunUtc = now;
    StateStore.Save(state);
    Console.WriteLine("No candidate cleared the AI decision gate; email suppressed.");
    return 0;
}

var apiKey = Environment.GetEnvironmentVariable("RESEND_API_KEY");
var recipient = Environment.GetEnvironmentVariable("MAIL_TO");
var sender = Environment.GetEnvironmentVariable("MAIL_FROM") ?? "digest@resend.dev";
if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(recipient))
{
    Console.Error.WriteLine("Missing RESEND_API_KEY or MAIL_TO.");
    return 1;
}

var markdown = DigestComposer.BuildMarkdown(profile, candidates, briefing, now);
var pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();
var rendered = Markdown.ToHtml(markdown, pipeline);
var html = BuildHtml(rendered);

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

var payload = JsonSerializer.Serialize(new
{
    from = sender,
    to = new[] { recipient },
    subject = DigestComposer.BuildSubject(profile, briefing),
    text = markdown,
    html
});

var response = await http.PostAsync(
    "https://api.resend.com/emails",
    new StringContent(payload, Encoding.UTF8, "application/json"));
var responseBody = await response.Content.ReadAsStringAsync();
if (!response.IsSuccessStatusCode)
{
    Console.Error.WriteLine($"Email failed: {response.StatusCode} - {responseBody}");
    return 1;
}

StateStore.MarkReviewed(state, candidates.Select(item => item.Article), displayed, now);
state.LastRunUtc = now;
state.LastDigestUtc = now;
StateStore.Save(state);
Console.WriteLine($"Email sent with {displayed.Count} material item(s).");
return 0;

static string BuildHtml(string renderedMarkdown) => $$"""
    <!DOCTYPE html>
    <html lang="en">
    <head>
      <meta charset="utf-8">
      <meta name="viewport" content="width=device-width, initial-scale=1">
      <style>
        body { margin: 0; background: #f5f7fb; color: #172033; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; line-height: 1.55; }
        main { max-width: 720px; margin: 0 auto; padding: 32px 22px 48px; background: #ffffff; }
        h1 { margin: 0 0 8px; font-size: 28px; color: #111827; }
        h2 { margin-top: 34px; padding-top: 18px; border-top: 1px solid #e5e7eb; color: #1d4ed8; }
        h3 { margin: 24px 0 10px; font-size: 18px; }
        a { color: #1d4ed8; text-decoration: none; }
        blockquote { margin: 20px 0; padding: 14px 18px; background: #eff6ff; border-left: 4px solid #2563eb; color: #1e3a8a; }
        code { padding: 2px 5px; border-radius: 4px; background: #f3f4f6; }
        hr { border: 0; border-top: 1px solid #e5e7eb; margin-top: 34px; }
      </style>
    </head>
    <body><main>{{renderedMarkdown}}</main></body>
    </html>
    """;
