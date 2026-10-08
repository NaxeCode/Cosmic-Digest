using System.Text;
using System.Reflection;

public sealed class ComplimentaryAllowanceTests
{
    [Fact]
    public void Reservation_requires_matching_day_and_lease_and_cannot_be_replayed()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".json");
        File.WriteAllText(path, """{"version":1,"days":{"2026-10-08":{"lease":"run-1","reserved_tokens":25000}}}""");
        var now = DateTimeOffset.Parse("2026-10-08T12:00:00Z");
        try
        {
            Assert.Throws<InvalidOperationException>(()=>ComplimentaryAllowance.Claim(path,"other",now));
            Assert.Throws<InvalidOperationException>(()=>ComplimentaryAllowance.Claim(path,"run-1",now.AddDays(1)));
            Assert.Throws<InvalidOperationException>(()=>ComplimentaryAllowance.Claim(path,"run-1",now.Date.AddHours(23).AddMinutes(59)));
            ComplimentaryAllowance.Claim(path,"run-1",now);
            Assert.Throws<IOException>(()=>ComplimentaryAllowance.Claim(path,"run-1",now));
        }
        finally { File.Delete(path); File.Delete(path+".consumed-2026-10-08"); }
    }

    [Fact]
    public void Input_and_output_must_fit_before_dispatch()
    {
        ComplimentaryAllowance.ValidateSize("system","news",Encoding.UTF8.GetBytes("{}"));
        Assert.Throws<InvalidOperationException>(()=>ComplimentaryAllowance.ValidateSize("system",new string('x',25000),[]));
        Assert.Throws<InvalidOperationException>(()=>ComplimentaryAllowance.ValidateSize("system",new string('界',8000),[]));
    }

    [Fact]
    public void Private_profile_does_not_enter_shared_prompt()
    {
        var profile = new BriefingProfile { Objective="private objective", Exclusions=["private exclusion"], Priorities=[new BriefingPriority {Name="private priority",WhyItMatters="private reason"}] };
        var method=typeof(NewsAi).GetMethod("BuildSystemPrompt",BindingFlags.NonPublic|BindingFlags.Static)!;
        var prompt=(string)method.Invoke(null,[profile])!;
        foreach(var secret in new[]{"private objective","private exclusion","private priority","private reason"})
            Assert.DoesNotContain(secret,prompt);
    }
}
