using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var dataDirectory = Environment.GetEnvironmentVariable("FEEDBACK_DATA_DIR") ?? "./feedback-data";
var journal = new JsonLineJournal(dataDirectory);
const int MaximumWebhookBytes = 256 * 1024;
const int MaximumFeedbackBytes = 8 * 1024;

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

app.MapGet("/feedback", (HttpRequest request) =>
{
    var token = request.Query["token"].ToString();
    var signingKey = Environment.GetEnvironmentVariable("FEEDBACK_SIGNING_KEY");
    if (!FeedbackTokenService.TryValidate(token, signingKey, DateTimeOffset.UtcNow, out var payload))
        return Results.Content(ResponsePage("That feedback link is invalid or expired."), "text/html", Encoding.UTF8, 400);

    return Results.Content(
        ConfirmationPage(payload!.Signal, token),
        "text/html",
        Encoding.UTF8,
        200);
});

app.MapPost("/feedback", async (HttpRequest request) =>
{
    if (request.ContentType?.StartsWith(
            "application/x-www-form-urlencoded",
            StringComparison.OrdinalIgnoreCase) != true)
        return Results.Content(ResponsePage("That feedback request is invalid."), "text/html", Encoding.UTF8, 400);

    if (request.ContentLength is > MaximumFeedbackBytes)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    string rawForm;
    try
    {
        rawForm = await BoundedBodyReader.ReadUtf8Async(
            request.Body,
            MaximumFeedbackBytes,
            request.HttpContext.RequestAborted);
    }
    catch (InvalidDataException)
    {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    }

    var form = QueryHelpers.ParseQuery(rawForm);
    var token = form["token"].ToString();
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
    if (request.ContentLength is > MaximumWebhookBytes)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    string rawPayload;
    try
    {
        rawPayload = await BoundedBodyReader.ReadUtf8Async(
            request.Body,
            MaximumWebhookBytes,
            request.HttpContext.RequestAborted);
    }
    catch (InvalidDataException)
    {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    }

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

static string ConfirmationPage(string signal, string token)
{
    var label = signal switch
    {
        "useful" => "Useful",
        "noise" => "Noise",
        "wrong" => "Wrong",
        "acted" => "I acted",
        _ => "Feedback"
    };
    var encodedLabel = System.Net.WebUtility.HtmlEncode(label);
    var encodedToken = System.Net.WebUtility.HtmlEncode(token);
    return $$"""
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Stella · Cosmic Digest</title></head>
        <body style="margin:0;background:#0b1020;color:#eef1ff;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;display:grid;min-height:100vh;place-items:center;">
          <main style="max-width:520px;padding:40px;text-align:center;">
            <img src="https://raw.githubusercontent.com/NaxeCode/Cosmic-Digest/main/assets/brand/stella-avatar-128.png" width="72" height="72" alt="Stella" style="border-radius:50%;">
            <h1 style="font-size:25px;margin:18px 0 8px;">Confirm {{encodedLabel}}</h1>
            <p style="color:#b9c2dc;line-height:1.6;margin:0 0 24px;">Email scanners may open links automatically. Stella records this signal only after you confirm it.</p>
            <form method="post" action="">
              <input type="hidden" name="token" value="{{encodedToken}}">
              <button type="submit" style="border:0;border-radius:10px;background:#8b7cff;color:#fff;font:600 16px inherit;padding:12px 20px;cursor:pointer;">Record {{encodedLabel}}</button>
            </form>
          </main>
        </body></html>
        """;
}
