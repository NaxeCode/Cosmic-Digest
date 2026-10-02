using System.Text;

public static class BoundedBodyReader
{
    public static async Task<string> ReadUtf8Async(
        Stream input,
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        if (maximumBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        using var bounded = new MemoryStream(capacity: Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[Math.Min(maximumBytes, 81920)];
        while (true)
        {
            var remaining = maximumBytes - checked((int)bounded.Length);
            var read = await input.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, (long)remaining + 1)),
                cancellationToken);
            if (read == 0)
                break;
            if (read > remaining)
                throw new InvalidDataException($"Request body exceeded the {maximumBytes}-byte safety limit.");
            await bounded.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return Encoding.UTF8.GetString(bounded.GetBuffer(), 0, checked((int)bounded.Length));
    }
}
