using System.Text;

public sealed class BoundedBodyReaderTests
{
    [Fact]
    public async Task ReadUtf8_returns_a_body_within_the_limit()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"type\":\"email.delivered\"}"));

        var body = await BoundedBodyReader.ReadUtf8Async(stream, 256, CancellationToken.None);

        Assert.Equal("{\"type\":\"email.delivered\"}", body);
    }

    [Fact]
    public async Task ReadUtf8_rejects_a_stream_before_buffering_beyond_the_limit()
    {
        await using var stream = new MemoryStream(new byte[257]);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            BoundedBodyReader.ReadUtf8Async(stream, 256, CancellationToken.None));

        Assert.Contains("256-byte", error.Message);
        Assert.Equal(257, stream.Position);
    }
}
