using System.Diagnostics;
using DotNetEnv;

Env.Load(".env");

if (args.Contains("--preview", StringComparer.OrdinalIgnoreCase))
    return EmailPreviewWriter.Write();

var runTimer = Stopwatch.StartNew();
var now = DateTimeOffset.UtcNow;
var profile = BriefingProfileLoader.Load();
var state = StateStore.Load();
var apiKey = Environment.GetEnvironmentVariable("RESEND_API_KEY");
using var resend = new ResendEmailClient();
if (await ReconcilePendingDeliveriesAsync(state, apiKey, resend))
    StateStore.Save(state);
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
    state.ReviewedEvents.Select(item => item.EventKey),
    state.ReviewedEvents.Select(item => item.Title));

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
var pendingSend = DigestIdempotency.Prepare(state, displayed, now);
StateStore.Save(state);
var idempotencyKey = pendingSend.IdempotencyKey;

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

DigestIdempotency.Complete(state, idempotencyKey);
StateStore.RecordDelivery(state, new DeliveryAttempt(
    sendResult.EmailId,
    now,
    subject,
    deliveryStatus,
    DateTimeOffset.UtcNow,
    idempotencyKey));

if (ResendDeliveryStatus.IsRetryableFailure(deliveryStatus))
{
    runTimer.Stop();
    metrics.DurationMilliseconds = runTimer.ElapsedMilliseconds;
    StateStore.RecordRun(state, metrics);
    state.LastRunUtc = now;
    StateStore.Save(state);
    Console.Error.WriteLine($"Email was accepted but entered terminal delivery state '{deliveryStatus}'. Candidates remain eligible.");
    return 1;
}

if (ResendDeliveryStatus.IsComplaint(deliveryStatus))
{
    StateStore.MarkReviewed(state, reviewedThisRun, displayed, now, sendResult.EmailId);
    state.LastRunUtc = now;
    state.LastDigestUtc = now;
    runTimer.Stop();
    metrics.DurationMilliseconds = runTimer.ElapsedMilliseconds;
    StateStore.RecordRun(state, metrics);
    StateStore.Save(state);
    Console.Error.WriteLine("Email received a complaint; its events remain reviewed and will not be retried.");
    return 1;
}

StateStore.MarkReviewed(state, reviewedThisRun, displayed, now, sendResult.EmailId);
state.LastRunUtc = now;
state.LastDigestUtc = now;
runTimer.Stop();
metrics.DurationMilliseconds = runTimer.ElapsedMilliseconds;
StateStore.RecordRun(state, metrics);
StateStore.Save(state);
Console.WriteLine($"Email {deliveryStatus} with {displayed.Count} material item(s); Resend id: {sendResult.EmailId}.");
return 0;

static async Task<bool> ReconcilePendingDeliveriesAsync(
    StateOfWorld state,
    string? apiKey,
    ResendEmailClient resend,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(apiKey))
        return false;

    var changed = false;
    foreach (var delivery in state.Deliveries
        .Where(item => ResendDeliveryStatus.IsPending(item.Status))
        .ToList())
    {
        try
        {
            var latest = await resend.GetStatusAsync(
                apiKey,
                delivery.EmailId,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(latest)
                || string.Equals(latest, delivery.Status, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            StateStore.RecordDelivery(state, delivery with
            {
                Status = latest,
                StatusAtUtc = DateTimeOffset.UtcNow
            });
            changed = true;
            if (ResendDeliveryStatus.IsRetryableFailure(latest)
                && StateStore.RestoreEligibilityForFailedDelivery(state, delivery.EmailId))
            {
                Console.Error.WriteLine(
                    $"Delivery {delivery.EmailId} later entered '{latest}'; its included events are eligible again.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine(
                $"Pending delivery reconciliation failed for {delivery.EmailId}: {ex.Message}");
        }
    }

    return changed;
}
