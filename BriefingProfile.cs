using System.Text;
using System.Text.Json;

public sealed class BriefingPriority
{
    public string Name { get; set; } = "";
    public int Weight { get; set; } = 3;
    public List<string> Signals { get; set; } = new();
    public string WhyItMatters { get; set; } = "";
}

public sealed class BriefingProfile
{
    public string Version { get; set; } = "legacy-env";
    public string DisplayName { get; set; } = "Your";
    public string Objective { get; set; } =
        "Surface credible external changes that materially improve decisions or capability per unit of attention.";
    public List<BriefingPriority> Priorities { get; set; } = new();
    public List<string> Regions { get; set; } = new();
    public List<string> TrustedDomains { get; set; } = new();
    public List<string> Exclusions { get; set; } = new()
    {
        "repeated stories",
        "generic hype without a concrete change",
        "rumors without credible evidence",
        "content with no plausible decision or capability impact"
    };
    public List<string> Feeds { get; set; } = new();
    public int LookbackHours { get; set; } = 36;
    public int CandidateLimit { get; set; } = 18;
    public int MaxItems { get; set; } = 5;
    public double MinimumScore { get; set; } = 1.5;
}

public static class BriefingProfileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static BriefingProfile Load()
    {
        var base64 = Environment.GetEnvironmentVariable("DIGEST_PROFILE_B64");
        var path = Environment.GetEnvironmentVariable("DIGEST_PROFILE_PATH");

        BriefingProfile profile;
        if (!string.IsNullOrWhiteSpace(base64))
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            profile = Deserialize(json, "DIGEST_PROFILE_B64");
        }
        else if (!string.IsNullOrWhiteSpace(path))
        {
            profile = Deserialize(File.ReadAllText(path), path);
        }
        else
        {
            profile = FromLegacyEnvironment();
        }

        Normalize(profile);
        return profile;
    }

    private static BriefingProfile Deserialize(string json, string source) =>
        JsonSerializer.Deserialize<BriefingProfile>(json, JsonOptions)
        ?? throw new InvalidOperationException($"The briefing profile from {source} was empty.");

    private static BriefingProfile FromLegacyEnvironment()
    {
        var topics = ParseCsv("PREF_TOPICS");
        var keywords = ParseCsv("PREF_KEYWORDS");

        var profile = new BriefingProfile
        {
            Version = "legacy-env",
            DisplayName = Environment.GetEnvironmentVariable("DISPLAY_NAME") ?? "Your",
            Regions = ParseCsv("PREF_REGIONS"),
            Feeds = ParseCsv("RSS_FEEDS")
        };

        foreach (var topic in topics)
        {
            profile.Priorities.Add(new BriefingPriority
            {
                Name = topic,
                Weight = 3,
                Signals = new List<string> { topic }
            });
        }

        if (keywords.Count > 0)
        {
            profile.Priorities.Add(new BriefingPriority
            {
                Name = "Configured watchlist",
                Weight = 4,
                Signals = keywords
            });
        }

        return profile;
    }

    private static List<string> ParseCsv(string key) =>
        (Environment.GetEnvironmentVariable(key) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void Normalize(BriefingProfile profile)
    {
        profile.Version = string.IsNullOrWhiteSpace(profile.Version) ? "unversioned" : profile.Version.Trim();
        profile.DisplayName = SingleLine(profile.DisplayName, "Your");
        profile.Objective = SingleLine(
            profile.Objective,
            "Surface credible external changes that materially improve decisions or capability per unit of attention.");
        profile.LookbackHours = Math.Clamp(profile.LookbackHours, 6, 168);
        profile.CandidateLimit = Math.Clamp(profile.CandidateLimit, 5, 40);
        profile.MaxItems = Math.Clamp(profile.MaxItems, 1, 6);
        profile.MinimumScore = Math.Clamp(profile.MinimumScore, 0.5, 20);

        profile.Priorities ??= new();
        profile.Priorities = profile.Priorities
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p =>
            {
                p.Name = p.Name.Trim();
                p.Weight = Math.Clamp(p.Weight, 1, 5);
                p.Signals ??= new();
                p.Signals = p.Signals
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                p.WhyItMatters = SingleLine(p.WhyItMatters, "Matches a current priority.");
                return p;
            })
            .Where(p => p.Signals.Count > 0)
            .ToList();

        profile.Regions = Clean(profile.Regions ?? new());
        profile.TrustedDomains = Clean(profile.TrustedDomains ?? new())
            .Select(d => d.TrimStart('.').ToLowerInvariant())
            .ToList();
        profile.Exclusions = Clean(profile.Exclusions ?? new());
        profile.Feeds = Clean(profile.Feeds ?? new())
            .Where(IsHttpUrl)
            .ToList();

        if (profile.Priorities.Count == 0)
            throw new InvalidOperationException("The briefing profile must define at least one priority with one signal.");
        if (profile.Feeds.Count == 0)
            throw new InvalidOperationException("No RSS feeds are configured in the briefing profile or RSS_FEEDS.");
    }

    private static List<string> Clean(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string SingleLine(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
