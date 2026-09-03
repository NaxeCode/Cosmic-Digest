using System.Net;
using System.Text;

public sealed class ResendEmailClientTests
{
    [Fact]
    public async Task Send_uses_idempotency_and_returns_resend_id()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("digest-20260903", request.Headers.GetValues("Idempotency-Key").Single());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"email-123\"}", Encoding.UTF8, "application/json")
            };
        });
        using var http = new HttpClient(handler);
        using var client = new ResendEmailClient(http);

        var result = await client.SendAsync(
            "re_test",
            "Stella <digest@example.com>",
            "reader@example.com",
            "Subject",
            "Text",
            "<p>HTML</p>",
            "digest-20260903");

        Assert.Equal("email-123", result.EmailId);
        Assert.Equal("accepted", result.Status);
    }

    [Fact]
    public async Task GetStatus_reads_last_event()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"last_event\":\"delivered\"}", Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        using var client = new ResendEmailClient(http);

        Assert.Equal("delivered", await client.GetStatusAsync("re_test", "email-123"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
