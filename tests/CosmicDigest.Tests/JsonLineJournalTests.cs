public sealed class JsonLineJournalTests
{
    [Fact]
    public async Task AppendUnique_uses_the_atomic_event_record_as_the_deduplication_marker()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cosmic-journal-{Guid.NewGuid():N}");
        try
        {
            var journal = new JsonLineJournal(directory);

            var first = await journal.AppendUniqueAsync(
                "feedback",
                "same-id",
                new { id = "same-id", signal = "useful" },
                CancellationToken.None);
            var duplicate = await journal.AppendUniqueAsync(
                "feedback",
                "same-id",
                new { id = "same-id", signal = "wrong" },
                CancellationToken.None);
            var records = await journal.ReadAsync(
                "feedback",
                CancellationToken.None);

            Assert.True(first);
            Assert.False(duplicate);
            Assert.Equal("useful", Assert.Single(records).GetProperty("signal").GetString());
            Assert.False(File.Exists(Path.Combine(directory, "feedback-ids.txt")));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Separate_journal_instances_coordinate_writes_through_the_shared_lock_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cosmic-journal-{Guid.NewGuid():N}");
        try
        {
            var first = new JsonLineJournal(directory);
            var second = new JsonLineJournal(directory);
            var writes = Enumerable.Range(0, 24)
                .Select(index => (index % 2 == 0 ? first : second).AppendUniqueAsync(
                    "feedback",
                    $"id-{index}",
                    new { id = $"id-{index}", signal = "useful" },
                    CancellationToken.None));

            var results = await Task.WhenAll(writes);
            var records = await first.ReadAsync("feedback", CancellationToken.None);

            Assert.All(results, Assert.True);
            Assert.Equal(24, records.Count);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
