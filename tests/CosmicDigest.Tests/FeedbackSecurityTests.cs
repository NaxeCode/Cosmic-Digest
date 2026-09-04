using System.Security.Cryptography;
using System.Text;

public sealed class FeedbackSecurityTests
{
    [Fact]
    public void Feedback_tokens_round_trip_and_reject_tampering()
    {
        var now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var url = FeedbackTokenService.CreateUrl(
            "https://feedback.example.com/feedback",
            "signing-secret",
            "event-42",
            "useful",
            now.AddDays(30));
        var token = ExtractToken(url!);

        Assert.True(FeedbackTokenService.TryValidate(token, "signing-secret", now, out var payload));
        Assert.Equal("event-42", payload!.EventKey);
        Assert.Equal("useful", payload.Signal);
        Assert.False(FeedbackTokenService.TryValidate(token + "x", "signing-secret", now, out _));
        Assert.False(FeedbackTokenService.TryValidate(token, "wrong-secret", now, out _));
    }

    [Fact]
    public void Feedback_tokens_expire()
    {
        var now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var url = FeedbackTokenService.CreateUrl(
            "https://feedback.example.com/feedback",
            "signing-secret",
            "event-42",
            "noise",
            now.AddMinutes(1));
        var token = ExtractToken(url!);

        Assert.False(FeedbackTokenService.TryValidate(token, "signing-secret", now.AddMinutes(2), out _));
    }

    [Fact]
    public void Feedback_journal_identity_is_stable_across_signals_for_one_event()
    {
        Assert.Equal(
            FeedbackTokenService.JournalIdForEvent("event-42"),
            FeedbackTokenService.JournalIdForEvent(" EVENT-42 "));
        Assert.NotEqual(
            FeedbackTokenService.JournalIdForEvent("event-42"),
            FeedbackTokenService.JournalIdForEvent("event-43"));
    }

    [Fact]
    public void Resend_webhook_verifier_accepts_svix_signature_and_rejects_replays_outside_tolerance()
    {
        var now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var id = "msg_test";
        var timestamp = now.ToUnixTimeSeconds().ToString();
        var payload = "{\"type\":\"email.delivered\"}";
        var secretBytes = Encoding.UTF8.GetBytes("webhook-test-secret-32-bytes!!");
        var secret = "whsec_" + Convert.ToBase64String(secretBytes);
        var signed = Encoding.UTF8.GetBytes($"{id}.{timestamp}.{payload}");
        var signature = "v1," + Convert.ToBase64String(HMACSHA256.HashData(secretBytes, signed));

        Assert.True(ResendWebhookVerifier.Verify(payload, secret, id, timestamp, signature, now));
        Assert.False(ResendWebhookVerifier.Verify(payload, secret, id, timestamp, signature, now.AddMinutes(6)));
        Assert.False(ResendWebhookVerifier.Verify(payload + "x", secret, id, timestamp, signature, now));
    }

    [Fact]
    public void Resend_webhook_verifier_rejects_out_of_range_unix_timestamps()
    {
        Assert.False(ResendWebhookVerifier.Verify(
            "{}",
            "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("secret")),
            "msg_test",
            long.MaxValue.ToString(),
            "v1,invalid",
            DateTimeOffset.UtcNow));
    }

    private static string ExtractToken(string url) =>
        Uri.UnescapeDataString(new Uri(url).Query.TrimStart('?').Split("token=", 2)[1]);
}
