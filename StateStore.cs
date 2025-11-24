// StateStore.cs
using System.Text.Json;

public static class StateStore
{
    static readonly string PathFile = "/data/state.json";
    static readonly JsonSerializerOptions J = new() { WriteIndented = true };

    public static StateOfWorld Load()
    {
        if (!File.Exists(PathFile)) return new StateOfWorld();
        return JsonSerializer.Deserialize<StateOfWorld>(File.ReadAllText(PathFile), J) ?? new StateOfWorld();
    }

    public static void Save(StateOfWorld s)
    {
        Directory.CreateDirectory("/data");
        File.WriteAllText(PathFile, JsonSerializer.Serialize(s, J));
    }

    public static void AppendNews(StateOfWorld s, IEnumerable<NewsItem> items, int keepDays = 10)
    {
        s.CacheNews.AddRange(items);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-keepDays);
        s.CacheNews = s.CacheNews.Where(n => n.Published >= cutoff).DistinctBy(n => n.Link).ToList();
    }

    public static void UpsertPrice(StateOfWorld s, PriceItem item)
    {
        var existing = s.Prices.FirstOrDefault(p => p.Name == item.Name && p.Url == item.Url);
        if (existing is null) s.Prices.Add(item);
        else existing.Series = item.Series;
    }
}
