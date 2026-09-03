using System.Diagnostics;
using DotNetEnv;

Env.Load(".env");

if (args.Contains("--preview", StringComparer.OrdinalIgnoreCase))
    return EmailPreviewWriter.Write();
var prepareOnly = args.Contains("--prepare-only", StringComparer.OrdinalIgnoreCase);
var deliverPendingOnly = args.Contains("--deliver-pending", StringComparer.OrdinalIgnoreCase);
if (prepareOnly && deliverPendingOnly)
    throw new ArgumentException("Choose either --prepare-only or --deliver-pending, not both.");

var runTimer = Stopwatch.StartNew();
var now = DateTimeOffset.UtcNow;
var state = StateStore.Load();
var apiKey = Environment.GetEnvironmentVariable("RESEND_API_KEY");
var outboxEncryptionKey = Environment.GetEnvironmentVariable("OUTBOX_ENCRYPTION_KEY");
using var resend = new ResendEmailClient();
if (await ReconcilePendingDeliveriesAsync(state, apiKey, resend))
    StateStore.Save(state);
if (state.PendingDigestSends.Count > 0
    && (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(outboxEncryptionKey)))
{
    Console.Error.WriteLine(
        "A pending digest requires RESEND_API_KEY and the stable OUTBOX_ENCRYPTION_KEY.");
    return 1;
}
if (prepareOnly && state.PendingDigestSends.Count > 0)
{
    Console.WriteLine("A durable pending digest is already prepared; delivery remains a separate step.");
    return 0;
}
if (!prepareOnly
    && !string.IsNullOrWhiteSpace(apiKey)
    && !string.IsNullOrWhiteSpace(outboxEncryptionKey))
{
    var replayExitCode = await ReplayPendingDigestAsync(
        state,
        apiKey,
        outboxEncryptionKey,
        resend);
    if (replayExitCode is not null)
        return replayExitCode.Value;
}
if (deliverPendingOnly)
{
    Console.WriteLine("No pending digest is waiting for delivery.");
    return 0;
}
var profile = BriefingProfileLoader.Load();
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
var retryArticles = state.DeliveryRetries.Select(item => item.Article).ToList();
var candidates = ArticleSelector.Rank(
    state.CacheNews.Concat(retryArticles),
    profile,
    state.ReviewedArticles.Select(item => item.Link),
    now,
    candidateCutoff,
    state.ReviewedEvents.Select(item => item.EventKey),
    state.ReviewedEvents.Select(item => item.Title),
    retryArticles.Select(item => item.Link));

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
var retryEventKeys = retryArticles
    .Select(EventIdentity.KeyFor)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var reviewedThisRun = ReviewPolicy.CandidatesToMarkReviewed(
    candidates,
    displayed,
    allCandidatesEvaluated,
    retryEventKeys);
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
if (string.IsNullOrWhiteSpace(apiKey)
    || string.IsNullOrWhiteSpace(recipient)
    || string.IsNullOrWhiteSpace(outboxEncryptionKey))
{
    Console.Error.WriteLine("Missing RESEND_API_KEY, MAIL_TO, or OUTBOX_ENCRYPTION_KEY.");
    return 1;
}

var subject = DigestComposer.BuildSubject(profile, briefing);
var text = DigestComposer.BuildMarkdown(profile, candidates, briefing, now);
var html = DigestComposer.BuildHtml(profile, candidates, briefing, now);
var preparedSend = DigestIdempotency.Prepare(
    state,
    reviewedThisRun,
    displayed,
    now,
    outboxEncryptionKey,
    new PendingEmailPayload(sender, recipient, subject, text, html));
StateStore.Save(state);
var pendingSend = preparedSend.Outbox;
var payload = preparedSend.Payload;
var reviewedForDelivery = DigestIdempotency.ReviewedCandidates(pendingSend);
var displayedForDelivery = DigestIdempotency.ReviewedCandidates(pendingSend, included: true);
var idempotencyKey = pendingSend.IdempotencyKey;
if (prepareOnly)
{
    Console.WriteLine(
        $"Prepared {displayedForDelivery.Count} material item(s) in the durable outbox; no delivery attempted.");
    return 0;
}

ResendSendResult sendResult;
try
{
    sendResult = await resend.SendAsync(
        apiKey,
        payload.Sender,
        payload.Recipient,
        payload.Subject,
        payload.Text,
        payload.Html,
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
var delivery = new DeliveryAttempt(
    sendResult.EmailId,
    now,
    "Cosmic Digest",
    deliveryStatus,
    DateTimeOffset.UtcNow,
    idempotencyKey,
    displayedForDelivery.Select(item => item.Article).ToList());
StateStore.RecordDelivery(state, delivery);

if (ResendDeliveryStatus.IsRetryableFailure(deliveryStatus))
{
    StateStore.MarkReviewed(
        state,
        reviewedForDelivery,
        displayedForDelivery,
        now,
        sendResult.EmailId);
    StateStore.RestoreEligibilityForFailedDelivery(state, delivery, DateTimeOffset.UtcNow);
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
    StateStore.MarkReviewed(state, reviewedForDelivery, displayedForDelivery, now, sendResult.EmailId);
    StateStore.CompleteDeliveryRetries(state, displayedForDelivery);
    state.LastRunUtc = now;
    state.LastDigestUtc = now;
    runTimer.Stop();
    metrics.DurationMilliseconds = runTimer.ElapsedMilliseconds;
    StateStore.RecordRun(state, metrics);
    StateStore.Save(state);
    Console.Error.WriteLine("Email received a complaint; its events remain reviewed and will not be retried.");
    return 1;
}

StateStore.MarkReviewed(state, reviewedForDelivery, displayedForDelivery, now, sendResult.EmailId);
StateStore.CompleteDeliveryRetries(state, displayedForDelivery);
state.LastRunUtc = now;
state.LastDigestUtc = now;
runTimer.Stop();
metrics.DurationMilliseconds = runTimer.ElapsedMilliseconds;
StateStore.RecordRun(state, metrics);
StateStore.Save(state);
Console.WriteLine($"Email {deliveryStatus} with {displayedForDelivery.Count} material item(s); Resend id: {sendResult.EmailId}.");
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
                && StateStore.RestoreEligibilityForFailedDelivery(
                    state,
                    delivery,
                    DateTimeOffset.UtcNow))
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

static async Task<int?> ReplayPendingDigestAsync(
    StateOfWorld state,
    string apiKey,
    string outboxEncryptionKey,
    ResendEmailClient resend,
    CancellationToken cancellationToken = default)
{
    var prepared = DigestIdempotency.ResumeOldest(state, outboxEncryptionKey);
    if (prepared is null)
        return null;

    var now = DateTimeOffset.UtcNow;
    var reviewed = DigestIdempotency.ReviewedCandidates(prepared.Outbox);
    var displayed = DigestIdempotency.ReviewedCandidates(prepared.Outbox, included: true);
    if (displayed.Count == 0)
        throw new InvalidOperationException("The pending digest outbox has no included items to replay.");

    ResendSendResult sendResult;
    try
    {
        sendResult = await resend.SendAsync(
            apiKey,
            prepared.Payload.Sender,
            prepared.Payload.Recipient,
            prepared.Payload.Subject,
            prepared.Payload.Text,
            prepared.Payload.Html,
            prepared.Outbox.IdempotencyKey,
            cancellationToken);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Pending email replay failed: {ex.Message}");
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
            deliveryStatus = await resend.WaitForLatestStatusAsync(
                apiKey,
                sendResult.EmailId,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Pending delivery verification unavailable; preserving accepted status: {ex.Message}");
        }
    }

    DigestIdempotency.Complete(state, prepared.Outbox.IdempotencyKey);
    var delivery = new DeliveryAttempt(
        sendResult.EmailId,
        now,
        "Cosmic Digest",
        deliveryStatus,
        DateTimeOffset.UtcNow,
        prepared.Outbox.IdempotencyKey,
        displayed.Select(item => item.Article).ToList());
    StateStore.RecordDelivery(state, delivery);
    StateStore.MarkReviewed(state, reviewed, displayed, now, sendResult.EmailId);

    var metrics = new RunMetrics
    {
        RunAtUtc = now,
        CandidateEventCount = reviewed.Count,
        SelectedEventCount = displayed.Count,
        SuppressedEventCount = Math.Max(0, reviewed.Count - displayed.Count),
        SelectionMode = "outbox_replay"
    };
    StateStore.RecordRun(state, metrics);
    state.LastRunUtc = now;

    if (ResendDeliveryStatus.IsRetryableFailure(deliveryStatus))
    {
        StateStore.RestoreEligibilityForFailedDelivery(state, delivery, DateTimeOffset.UtcNow);
        StateStore.Save(state);
        Console.Error.WriteLine(
            $"Pending email replay entered terminal delivery state '{deliveryStatus}'. Included events were queued for retry.");
        return 1;
    }

    StateStore.CompleteDeliveryRetries(state, displayed);
    state.LastDigestUtc = now;
    StateStore.Save(state);
    if (ResendDeliveryStatus.IsComplaint(deliveryStatus))
    {
        Console.Error.WriteLine(
            "Pending email replay received a complaint; its events remain reviewed and will not be retried.");
        return 1;
    }

    Console.WriteLine(
        $"Replayed pending email {deliveryStatus} with {displayed.Count} material item(s); Resend id: {sendResult.EmailId}.");
    return 0;
}
