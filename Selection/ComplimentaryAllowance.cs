using System.Text;
using System.Text.Json;

public static class ComplimentaryAllowance
{
    public const int DailyReservation = 25_000;
    public const int MaxOutputTokens = 3_000;
    // Conservative UTF-8 byte bound plus framing allowance; text-only requests.
    public static void ValidateSize(string system, string candidates, byte[] schema)
    {
        var upperBound = (long)Encoding.UTF8.GetByteCount(system)
            + Encoding.UTF8.GetByteCount(candidates) + schema.Length + 2_048 + MaxOutputTokens;
        if (upperBound > DailyReservation)
            throw new InvalidOperationException("The briefing exceeds its reserved token allowance.");
    }

    public static void Claim(string path, string lease, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(lease) || now.UtcDateTime.TimeOfDay >= TimeSpan.FromHours(24) - TimeSpan.FromMinutes(5))
            throw new InvalidOperationException("No current complimentary reservation is available.");
        using var ledger = JsonDocument.Parse(File.ReadAllText(path));
        var root = ledger.RootElement;
        var day = now.UtcDateTime.ToString("yyyy-MM-dd");
        if (root.GetProperty("version").GetInt32() != 1
            || !root.GetProperty("days").TryGetProperty(day, out var reservation)
            || reservation.GetProperty("lease").GetString() != lease
            || reservation.GetProperty("reserved_tokens").GetInt32() != DailyReservation)
            throw new InvalidOperationException("No current complimentary reservation is available.");
        // CreateNew gives one dispatch per committed reservation in this checkout.
        // Across CI checkouts the precommitted daily reservation rejects new leases.
        using var consumed = new FileStream(path + ".consumed-" + day, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }
}
