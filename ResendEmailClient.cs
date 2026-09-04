using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public sealed record ResendSendResult(string EmailId, string Status);

public static class ResendDeliveryStatus
{
    public static bool IsRetryableFailure(string? status) =>
        status is "bounced" or "suppressed" or "failed" or "canceled" or "request_rejected";

    public static bool IsComplaint(string? status) => status == "complained";

    public static bool IsTerminal(string? status) =>
        status == "delivered" || IsComplaint(status) || IsRetryableFailure(status);

    public static bool IsPending(string? status) =>
        status is "accepted" or "queued" or "scheduled" or "sent" or "delayed" or "delivery_delayed";
}

public sealed class ResendEmailClient : IDisposable
{
    private const string RejectedEmailPrefix = "rejected:";
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public ResendEmailClient(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    }

    public async Task<ResendSendResult> SendAsync(
        string apiKey,
        string sender,
        string recipient,
        string subject,
        string text,
        string html,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            from = sender,
            to = new[] { recipient },
            subject,
            text,
            html,
            tags = new[] { new { name = "category", value = "cosmic_digest" } }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        using var response = await _http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (IsPermanentRequestRejection(response.StatusCode))
            {
                Console.Error.WriteLine(
                    $"Resend permanently rejected the prepared request ({response.StatusCode}); " +
                    "the durable outbox will be retired so corrected sender/recipient configuration can rebuild it.");
                return new ResendSendResult(
                    RejectedEmailPrefix + idempotencyKey,
                    "request_rejected");
            }

            throw new InvalidOperationException(
                $"Resend rejected the email: {response.StatusCode} - {Compact(responseBody)}");
        }

        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("id", out var idElement)
            || string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            throw new InvalidOperationException("Resend accepted the request but returned no email id.");
        }

        return new ResendSendResult(idElement.GetString()!, "accepted");
    }

    public async Task<string> WaitForLatestStatusAsync(
        string apiKey,
        string emailId,
        CancellationToken cancellationToken = default)
    {
        if (emailId.StartsWith(RejectedEmailPrefix, StringComparison.Ordinal))
            return "request_rejected";

        var latest = "accepted";
        foreach (var delay in new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
        {
            await Task.Delay(delay, cancellationToken);
            latest = await GetStatusAsync(apiKey, emailId, cancellationToken) ?? latest;
            if (ResendDeliveryStatus.IsTerminal(latest))
                break;
        }
        return latest;
    }

    public async Task<string?> GetStatusAsync(
        string apiKey,
        string emailId,
        CancellationToken cancellationToken = default)
    {
        if (emailId.StartsWith(RejectedEmailPrefix, StringComparison.Ordinal))
            return "request_rejected";

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.resend.com/emails/{Uri.EscapeDataString(emailId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(responseBody);
        return document.RootElement.TryGetProperty("last_event", out var status)
            ? status.GetString()?.Trim().ToLowerInvariant()
            : null;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _http.Dispose();
    }

    private static bool IsPermanentRequestRejection(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is >= 400 and < 500
            && statusCode is not HttpStatusCode.RequestTimeout
            && statusCode is not HttpStatusCode.Conflict
            && code != 429;
    }

    private static string Compact(string value)
    {
        var compact = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 500 ? compact : compact[..500] + "…";
    }
}
