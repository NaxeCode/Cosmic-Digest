using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var dataDirectory = Environment.GetEnvironmentVariable("FEEDBACK_DATA_DIR") ?? "./feedback-data";
var journal = new JsonLineJournal(dataDirectory);

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

app.MapGet("/feedback", async (HttpRequest request) =>
{
    var token = request.Query["token"].ToString();
    var signingKey = Environment.GetEnvironmentVariable("FEEDBACK_SIGNING_KEY");
    if (!FeedbackTokenService.TryValidate(token, signingKey, DateTimeOffset.UtcNow, out var payload))
        return Results.Content(ResponsePage("That feedback link is invalid or expired."), "text/html", Encoding.UTF8, 400);

    var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    await journal.AppendUniqueAsync(
        "feedback",
        id,
        new
        {
            id,
            receivedAtUtc = DateTimeOffset.UtcNow,
            payload!.EventKey,
            payload.Signal
        },
        request.HttpContext.RequestAborted);

    return Results.Content(
        ResponsePage(payload!.Signal == "acted" ? "Action recorded. Keep moving." : "Signal recorded. Stella will use the evidence, not guesswork."),
        "text/html",
        Encoding.UTF8,
        200);
});

app.MapPost("/webhooks/resend", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    var rawPayload = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
    var messageId = request.Headers["svix-id"].ToString();
    var valid = ResendWebhookVerifier.Verify(
        rawPayload,
        Environment.GetEnvironmentVariable("RESEND_WEBHOOK_SECRET"),
        messageId,
        request.Headers["svix-timestamp"].ToString(),
        request.Headers["svix-signature"].ToString(),
        DateTimeOffset.UtcNow);
    if (!valid)
        return Results.BadRequest(new { error = "invalid webhook signature" });

    using var document = JsonDocument.Parse(rawPayload);
    var root = document.RootElement;
    var eventType = root.TryGetProperty("type", out var type) ? type.GetString() : "unknown";
    var createdAt = root.TryGetProperty("created_at", out var created)
        && DateTimeOffset.TryParse(created.GetString(), out var parsedCreated)
            ? parsedCreated
            : DateTimeOffset.UtcNow;
    var emailId = root.TryGetProperty("data", out var data)
        && data.TryGetProperty("email_id", out var email)
            ? email.GetString()
            : null;

    await journal.AppendUniqueAsync(
        "delivery",
        messageId,
        new
        {
            id = messageId,
            receivedAtUtc = DateTimeOffset.UtcNow,
            createdAtUtc = createdAt,
            type = eventType,
            emailId
        },
        request.HttpContext.RequestAborted);
    return Results.Ok(new { received = true });
});

app.MapGet("/metrics", async (HttpRequest request) =>
{
    var configuredToken = Environment.GetEnvironmentVariable("FEEDBACK_ADMIN_TOKEN");
    var suppliedToken = request.Headers["Authorization"].ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
    if (!SecureEquals(configuredToken, suppliedToken))
        return Results.Unauthorized();

    var feedback = await journal.ReadAsync("feedback", request.HttpContext.RequestAborted);
    var delivery = await journal.ReadAsync("delivery", request.HttpContext.RequestAborted);
    var feedbackCounts = feedback
        .Select(element => element.TryGetProperty("Signal", out var signal) ? signal.GetString() : null)
        .Where(signal => !string.IsNullOrWhiteSpace(signal))
        .GroupBy(signal => signal!, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    var latestDeliveryByEmail = delivery
        .Where(element => element.TryGetProperty("emailId", out var email) && !string.IsNullOrWhiteSpace(email.GetString()))
        .GroupBy(element => element.GetProperty("emailId").GetString()!, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderByDescending(element =>
            element.TryGetProperty("createdAtUtc", out var created) ? created.GetDateTimeOffset() : DateTimeOffset.MinValue).First())
        .ToList();

    return Results.Json(new
    {
        feedback = feedbackCounts,
        delivery = latestDeliveryByEmail
            .Select(element => element.TryGetProperty("type", out var type) ? type.GetString() : "unknown")
            .GroupBy(type => type)
            .ToDictionary(group => group.Key ?? "unknown", group => group.Count()),
        generatedAtUtc = DateTimeOffset.UtcNow
    });
});

app.Run();

static bool SecureEquals(string? expected, string? supplied)
{
    if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied))
        return false;
    var left = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
    var right = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
    return CryptographicOperations.FixedTimeEquals(left, right);
}

static string ResponsePage(string message) => $$"""
    <!doctype html>
    <html lang="en">
    <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Stella · Cosmic Digest</title></head>
    <body style="margin:0;background:#0b1020;color:#eef1ff;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;display:grid;min-height:100vh;place-items:center;">
      <main style="max-width:520px;padding:40px;text-align:center;">
        <img src="https://raw.githubusercontent.com/NaxeCode/Cosmic-Digest/main/assets/brand/stella-avatar-128.png" width="72" height="72" alt="Stella" style="border-radius:50%;">
        <h1 style="font-size:25px;margin:18px 0 8px;">Signal received</h1>
        <p style="color:#b9c2dc;line-height:1.6;margin:0;">{{System.Net.WebUtility.HtmlEncode(message)}}</p>
      </main>
    </body></html>
    """;

public sealed class JsonLineJournal
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _directory;

    public JsonLineJournal(string directory) => _directory = directory;

    public async Task<bool> AppendUniqueAsync(
        string stream,
        string id,
        object value,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_directory);
            var idsPath = Path.Combine(_directory, $"{stream}-ids.txt");
            var knownIds = File.Exists(idsPath)
                ? new HashSet<string>(await File.ReadAllLinesAsync(idsPath, cancellationToken), StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            if (!knownIds.Add(id))
                return false;

            await File.AppendAllTextAsync(
                Path.Combine(_directory, $"{stream}-events.jsonl"),
                JsonSerializer.Serialize(value) + Environment.NewLine,
                cancellationToken);
            await File.AppendAllTextAsync(idsPath, id + Environment.NewLine, cancellationToken);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyList<JsonElement>> ReadAsync(string stream, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var path = Path.Combine(_directory, $"{stream}-events.jsonl");
            if (!File.Exists(path))
                return Array.Empty<JsonElement>();
            var lines = await File.ReadAllLinesAsync(path, cancellationToken);
            return lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonSerializer.Deserialize<JsonElement>(line))
                .ToList();
        }
        finally
        {
            Gate.Release();
        }
    }
}
