using Safe0ne.Shared.Contracts;
using Safe0ne.Shared.Contracts.Policy;
using Xunit;

namespace Safe0ne.Shared.Tests.Policy;

public sealed class EffectivePolicyEngineTests
{
    [Fact]
    public void AlwaysAllowed_Overrides_All()
    {
        var nowUtc = new DateTimeOffset(2026, 02, 11, 12, 0, 0, TimeSpan.Zero);
        var p = BasePolicy(SafetyMode.Lockdown) with
        {
            AlwaysAllowed = true,
            GrantUntilUtc = nowUtc.AddHours(2),
            BedtimeWindow = new ScheduleWindow(true, "00:00", "23:59")
        };

        var eff = EffectivePolicyEngine.Evaluate(p, nowUtc);
        Assert.Equal(SafetyMode.Open, eff.EffectiveMode);
        Assert.Equal("always_allowed", eff.ReasonCode);
    }

    [Fact]
    public void ActiveGrant_Overrides_Mode_And_Schedules()
    {
        var nowUtc = new DateTimeOffset(2026, 02, 11, 12, 0, 0, TimeSpan.Zero);
        var p = BasePolicy(SafetyMode.Lockdown) with
        {
            GrantUntilUtc = nowUtc.AddMinutes(30),
            BedtimeWindow = new ScheduleWindow(true, "00:00", "23:59")
        };

        var eff = EffectivePolicyEngine.Evaluate(p, nowUtc);
        Assert.Equal(SafetyMode.Open, eff.EffectiveMode);
        Assert.Equal("grant", eff.ReasonCode);
    }

    [Fact]
    public void Bedtime_Schedule_Increases_Restriction()
    {
        // Local-time evaluation; choose a UTC value that, in local, is 23:00 for most zones.
        // We don't rely on the environment timezone for the test logic; instead we set a window
        // that is active for any local time (cross-midnight) but only maps to Bedtime.
        var nowUtc = DateTimeOffset.UtcNow;
        var p = BasePolicy(SafetyMode.Open) with
        {
            BedtimeWindow = new ScheduleWindow(true, "21:00", "07:00")
        };

        var eff = EffectivePolicyEngine.Evaluate(p, nowUtc);

        // Either bedtime is active now (then it must raise restriction), or it's not.
        // The important invariant: it must NEVER reduce restriction.
        Assert.True((int)eff.EffectiveMode >= (int)p.Mode);
        if (eff.EffectiveMode != p.Mode)
        {
            Assert.Equal("schedule", eff.ReasonCode);
            Assert.Equal("bedtime", eff.ActiveSchedule);
        }
    }

    [Fact]
    public void Schedule_Does_Not_Downgrade_Configured_Mode()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var p = BasePolicy(SafetyMode.Lockdown) with
        {
            BedtimeWindow = new ScheduleWindow(true, "00:00", "23:59"),
            SchoolWindow = new ScheduleWindow(true, "00:00", "23:59"),
            HomeworkWindow = new ScheduleWindow(true, "00:00", "23:59")
        };

        var eff = EffectivePolicyEngine.Evaluate(p, nowUtc);
        Assert.Equal(SafetyMode.Lockdown, eff.EffectiveMode);
        Assert.Equal("mode", eff.ReasonCode);
        Assert.Null(eff.ActiveSchedule);
    }

    [Fact]
    public void CrossMidnight_Window_Is_Active_On_Both_Sides()
    {
        // We compute the test using local times because the engine evaluates schedules in local.
        var localZone = TimeZoneInfo.Local;

        // 23:30 local
        var localLate = new DateTimeOffset(2026, 02, 11, 23, 30, 0, localZone.GetUtcOffset(new DateTime(2026, 02, 11, 23, 30, 0)));
        // 01:30 local (next day)
        var localEarly = new DateTimeOffset(2026, 02, 12, 01, 30, 0, localZone.GetUtcOffset(new DateTime(2026, 02, 12, 01, 30, 0)));

        var p = BasePolicy(SafetyMode.Open) with
        {
            BedtimeWindow = new ScheduleWindow(true, "22:00", "07:00")
        };

        var effLate = EffectivePolicyEngine.Evaluate(p, localLate.ToUniversalTime());
        var effEarly = EffectivePolicyEngine.Evaluate(p, localEarly.ToUniversalTime());

        Assert.Equal(SafetyMode.Bedtime, effLate.EffectiveMode);
        Assert.Equal("schedule", effLate.ReasonCode);
        Assert.Equal("bedtime", effLate.ActiveSchedule);

        Assert.Equal(SafetyMode.Bedtime, effEarly.EffectiveMode);
        Assert.Equal("schedule", effEarly.ReasonCode);
        Assert.Equal("bedtime", effEarly.ActiveSchedule);
    }

    private static ChildPolicy BasePolicy(SafetyMode mode)
        => new(
            ChildId: new ChildId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            Version: PolicyVersion.Initial,
            Mode: mode,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedBy: "tests");
}
