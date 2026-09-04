using System.Text;

public sealed class BriefingProfileLoaderTests
{
    [Fact]
    public void Load_normalizes_profile_and_rejects_non_http_feeds()
    {
        const string json = """
            {
              "version": "test-v1",
              "displayName": "Test\nReader",
              "objective": "High signal\nper unit of attention.",
              "priorities": [
                {
                  "name": "Backend",
                  "weight": 9,
                  "signals": [".NET", ".NET"],
                  "whyItMatters": "Reliable\nsystems."
                }
              ],
              "feeds": ["file:///tmp/private.xml", "https://example.com/feed"]
            }
            """;
        var previousBase64 = Environment.GetEnvironmentVariable("DIGEST_PROFILE_B64");
        var previousPath = Environment.GetEnvironmentVariable("DIGEST_PROFILE_PATH");

        try
        {
            Environment.SetEnvironmentVariable("DIGEST_PROFILE_PATH", null);
            Environment.SetEnvironmentVariable(
                "DIGEST_PROFILE_B64",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));

            var profile = BriefingProfileLoader.Load();

            Assert.Equal("Test Reader", profile.DisplayName);
            Assert.Equal("High signal per unit of attention.", profile.Objective);
            Assert.Equal(5, profile.Priorities[0].Weight);
            Assert.Single(profile.Priorities[0].Signals);
            Assert.Equal("Reliable systems.", profile.Priorities[0].WhyItMatters);
            Assert.Equal(new[] { "https://example.com/feed" }, profile.Feeds);
            Assert.Equal("example.com", Assert.Single(profile.Sources).Name);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DIGEST_PROFILE_B64", previousBase64);
            Environment.SetEnvironmentVariable("DIGEST_PROFILE_PATH", previousPath);
        }
    }
}
