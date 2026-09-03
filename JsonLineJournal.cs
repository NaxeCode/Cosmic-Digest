using System.Text;
using System.Text.Json;

public sealed class JsonLineJournal
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);
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
            var path = Path.Combine(_directory, $"{stream}-events.jsonl");
            var existing = await ReadValidLinesAsync(path, cancellationToken);
            if (existing.Any(line => TryReadId(line, out var knownId)
                && string.Equals(knownId, id, StringComparison.Ordinal)))
            {
                return false;
            }

            var serialized = JsonSerializer.Serialize(value);
            var replacement = existing.Append(serialized);
            var temporaryPath = Path.Combine(
                _directory,
                $".{stream}-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    string.Join('\n', replacement) + "\n",
                    Utf8WithoutBom,
                    cancellationToken);
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }

            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyList<JsonElement>> ReadAsync(
        string stream,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var path = Path.Combine(_directory, $"{stream}-events.jsonl");
            var lines = await ReadValidLinesAsync(path, cancellationToken);
            return lines
                .Select(line => JsonSerializer.Deserialize<JsonElement>(line))
                .ToList();
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<IReadOnlyList<string>> ReadValidLinesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return Array.Empty<string>();

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        return lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(IsValidJson)
            .ToList();
    }

    private static bool IsValidJson(string line)
    {
        try
        {
            using var _ = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadId(string line, out string? id)
    {
        using var document = JsonDocument.Parse(line);
        id = document.RootElement.TryGetProperty("id", out var property)
            ? property.GetString()
            : null;
        return !string.IsNullOrWhiteSpace(id);
    }
}
