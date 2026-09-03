using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DotNetEnv;

Env.Load(".env");

if (args.Contains("--preview", StringComparer.OrdinalIgnoreCase))
    return EmailPreviewWriter.Write();

var runTimer = Stopwatch.StartNew();
var now = DateTimeOffset.UtcNow;
var profile = BriefingProfileLoader.Load();
var state = StateStore.Load();
StateStore.PruneReviewed(state, now);

Console.WriteLine($"Profile: {profile.Version}; priorities: {profile.Priorities.Count}; sources: {profile.Sources.Count}");

var ingestion = await RssIngestor.FetchAsync(
    profile.Sources,
    state.FeedHealth,
    now,
    profile.FeedCircuitFailureThreshold,
    profile.FeedCircuitHours);
StateStore.UpdateFeedHealth(state, ingestion.Feeds, now);
var healthyFeeds = ingestion.Feeds.Count(feed => feed.IsHealthy);
Console.WriteLine($"Sources healthy: {healthyFeeds}/{ingestion.Feeds.Count}; fetched articles: {ingestion.Articles.Count}");

var keepDays = Math.Max(4, (int)Math.Ceiling(profile.LookbackHours / 24d) + 1);
StateStore.AppendNews(state, ingestion.Articles, keepDays);

var candidateCutoff = StateStore.ResolveCandidateCutoff(state, now, profile.LookbackHours);
var candidates = ArticleSelector.Rank(
    state.CacheNews,
    profile,
    state.ReviewedArticles.Select(item => item.Link),
    now,
    candidateCutoff,
    state.ReviewedEvents.Select(item => item.EventKey));

Console.WriteLine($"Candidate events above threshold: {candidates.Count}");
var metrics = new RunMetrics
{
    RunAtUtc = now,
    FeedCount = ingestion.Feeds.Count,
    HealthyFeedCount = healthyFeeds,
    FetchedArticleCount = ingestion.Articles.Count,
    CandidateEventCount = candidates.Count
};

if (candidates.Count == 0)
{
    runTimer.Stop();
    metrics.DurationMilliseconds = runTimer.ElapsedMilliseconds;
    StateStore.RecordRun(state, metrics);
    state.LastRunUtc = now;
    StateStore.Save(state);
    Console.WriteLine("No material new developments; email suppressed.");
    return 0;
}

BriefingDocument briefing;
var enableAi = Environment.GetEnvironmentVariable("ENABLE_AI_SUMMARY")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
var allCandidatesEvaluated = false;
if (enableAi)
{
    try
    {
        var aiResult = await NewsAi.BuildBriefingAsync(profile, candidates);
        briefing = aiResult.Briefing;
        allCandidatesEvaluated = true;
        metrics.SelectionMode = "ai";
        metrics.Model = aiResult.Model;
        metrics.ReasoningEffort = aiResult.ReasoningEffort;
        metrics.InputTokens = aiResult.InputTokens;
        metrics.OutputTokens = aiResult.OutputTokens;
        Console.WriteLine($"AI selected {briefing.Items.Count} material item(s); tokens: {aiResult.InputTokens ?? 0} in/{aiResult.OutputTokens ?? 0} out.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"AI briefing failed; using deterministic fallback: {ex.Message}");
        briefing = NewsAi.BuildDeterministicFallback(profile, candidates);
        metrics.SelectionMode = "deterministic_fallback";
    }
}
else
{
    briefing = NewsAi.BuildDeterministicFallback(profile, candidates);
}

var displayed = DigestComposer.DisplayedCandidates(candidates, briefing);
var reviewedThisRun = ReviewPolicy.CandidatesToMarkReviewed(candidates, displayed, allCandidatesEvaluated);
metrics.SelectedEventCount = displayed.Count;
metrics.SuppressedEventCount = Math.Max(0, candidates.Count - displayed.Count);
if (displayed.Count == 0)
{
    StateStore.MarkReviewed(state, reviewedThisRun, displayed, now);
    runTimer.Stop();
    metrics.DurationMilliseconds = runTimer.ElapsedMilliseconds;
    StateStore.RecordRun(state, metrics);
    state.LastRunUtc = now;
    StateStore.Save(state);
    Console.WriteLine("No candidate cleared the AI decision gate; email suppressed.");
    return 0;
}

var apiKey = Environment.GetEnvironmentVariable("RESEND_API_KEY");
var recipient = Environment.GetEnvironmentVariable("MAIL_TO");
var sender = Environment.GetEnvironmentVariable("MAIL_FROM") ?? "Stella · Cosmic Digest <digest@resend.dev>";
if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(recipient))
{
    Console.Error.WriteLine("Missing RESEND_API_KEY or MAIL_TO.");
    return 1;
}

var subject = DigestComposer.BuildSubject(profile, briefing);
var text = DigestComposer.BuildMarkdown(profile, candidates, briefing, now);
var html = DigestComposer.BuildHtml(profile, candidates, briefing, now);
var idempotencyKey = BuildIdempotencyKey(now, displayed);

using var resend = new ResendEmailClient();
ResendSendResult sendResult;
try
{
    sendResult = await resend.SendAsync(
        apiKey,
        sender,
        recipient,
        subject,
        text,
        html,
        idempotencyKey);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Email failed: {ex.Message}");
    return 1;
}

var deliveryStatus = sendResult.Status;
var verifyDelivery = !string.Equals(
    Environment.GetEnvironmentVariable("RESEND_VERIFY_DELIVERY"),
    "false",
    StringComparison.OrdinalIgnoreCase);
if (verifyDelivery)
{
    try
    {
        deliveryStatus = await resend.WaitForLatestStatusAsync(apiKey, sendResult.EmailId);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Delivery verification unavailable; preserving accepted status: {ex.Message}");
    }
}

StateStore.RecordDelivery(state, new DeliveryAttempt(
    sendResult.EmailId,
    now,
    subject,
    deliveryStatus,
    DateTimeOffset.UtcNow));

if (deliveryStatus is "bounced" or "complained" or "suppressed" or "failed" or "canceled")
{
    runTimer.Stop();
    metrics.DurationMilliseconds = runTimer.ElapsedMilliseconds;
    StateStore.RecordRun(state, metrics);
    state.LastRunUtc = now;
    StateStore.Save(state);
    Console.Error.WriteLine($"Email was accepted but entered terminal delivery state '{deliveryStatus}'. Candidates remain eligible.");
    return 1;
}

StateStore.MarkReviewed(state, reviewedThisRun, displayed, now);
state.LastRunUtc = now;
state.LastDigestUtc = now;
runTimer.Stop();
metrics.DurationMilliseconds = runTimer.ElapsedMilliseconds;
StateStore.RecordRun(state, metrics);
StateStore.Save(state);
Console.WriteLine($"Email {deliveryStatus} with {displayed.Count} material item(s); Resend id: {sendResult.EmailId}.");
return 0;

static string BuildIdempotencyKey(DateTimeOffset sentAtUtc, IReadOnlyList<ScoredArticle> displayed)
{
    var eventKeys = displayed
        .Select(item => string.IsNullOrWhiteSpace(item.EventKey)
            ? EventIdentity.KeyFor(item.Article)
            : item.EventKey)
        .Order(StringComparer.Ordinal);
    var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', eventKeys))))[..16]
        .ToLowerInvariant();
    return $"cosmic-digest-{sentAtUtc:yyyyMMdd}-{digest}";
}
