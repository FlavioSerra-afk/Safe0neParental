using System.Text.Json;
using Safe0ne.Shared.Contracts;
using Xunit;

namespace Safe0ne.Shared.Tests;

public sealed class ContractsSerializationTests
{
    [Fact]
    public void ChildPolicy_RoundTrips_With_Default_JsonOptions()
    {
        var id = ChildId.New();
        var policy = new ChildPolicy(
            ChildId: id,
            Version: PolicyVersion.Initial,
            Mode: SafetyMode.Homework,
            UpdatedAtUtc: DateTimeOffset.Parse("2026-02-02T12:00:00Z"),
            UpdatedBy: "tester",
            GrantUntilUtc: DateTimeOffset.Parse("2026-02-02T12:30:00Z"),
            AlwaysAllowed: false,
            BlockedProcessNames: new[] { "notepad.exe" },
            DailyScreenTimeLimitMinutes: 120,
            BedtimeWindow: new ScheduleWindow(true, "22:00", "07:00"));

        var json = JsonSerializer.Serialize(policy, JsonDefaults.Options);
        var parsed = JsonSerializer.Deserialize<ChildPolicy>(json, JsonDefaults.Options);

        Assert.NotNull(parsed);
        Assert.Equal(policy.ChildId.Value, parsed!.ChildId.Value);
        Assert.Equal(policy.Version.Value, parsed.Version.Value);
        Assert.Equal(policy.Mode, parsed.Mode);
        Assert.Equal(policy.UpdatedBy, parsed.UpdatedBy);
        Assert.Equal(policy.GrantUntilUtc, parsed.GrantUntilUtc);
        Assert.Equal(policy.AlwaysAllowed, parsed.AlwaysAllowed);
        Assert.Equal(policy.DailyScreenTimeLimitMinutes, parsed.DailyScreenTimeLimitMinutes);
        Assert.Equal(policy.BedtimeWindow, parsed.BedtimeWindow);
    }

    [Fact]
    public void PolicyVersion_Is_Monotonic()
    {
        var v1 = PolicyVersion.Initial;
        var v2 = v1.Next();
        var v3 = v2.Next();
        Assert.True(v2.Value > v1.Value);
        Assert.True(v3.Value > v2.Value);
    }

    [Fact]
    public void EffectiveChildState_And_RequestsModels_RoundTrip()
    {
        var id = ChildId.New();
        var grant = new Grant(
            GrantId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ChildId: id,
            Type: GrantType.ExtraScreenTime,
            Target: "screen_time",
            CreatedAtUtc: DateTimeOffset.Parse("2026-02-02T12:00:00Z"),
            ExpiresAtUtc: DateTimeOffset.Parse("2026-02-03T00:00:00Z"),
            ExtraMinutes: 15,
            SourceRequestId: Guid.Parse("33333333-3333-3333-3333-333333333333"));

        var effective = new EffectiveChildState(
            ChildId: id,
            PolicyVersion: PolicyVersion.Initial,
            ConfiguredMode: SafetyMode.Homework,
            EffectiveMode: SafetyMode.Homework,
            ReasonCode: "configured",
            EvaluatedAtUtc: DateTimeOffset.Parse("2026-02-02T12:00:00Z"),
            GrantUntilUtc: null,
            AlwaysAllowed: false,
            ActiveSchedule: null,
            ActiveGrants: new[] { grant });

        var req = new AccessRequest(
            RequestId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ChildId: id,
            Type: AccessRequestType.MoreTime,
            Target: "screen_time",
            Reason: "please",
            CreatedAtUtc: DateTimeOffset.Parse("2026-02-02T12:05:00Z"));

        var effJson = JsonSerializer.Serialize(effective, JsonDefaults.Options);
        var effParsed = JsonSerializer.Deserialize<EffectiveChildState>(effJson, JsonDefaults.Options);
        Assert.NotNull(effParsed);
        Assert.NotNull(effParsed!.ActiveGrants);
        Assert.Single(effParsed.ActiveGrants!);
        Assert.Equal(15, effParsed.ActiveGrants![0].ExtraMinutes);

        var reqJson = JsonSerializer.Serialize(req, JsonDefaults.Options);
        var reqParsed = JsonSerializer.Deserialize<AccessRequest>(reqJson, JsonDefaults.Options);
        Assert.NotNull(reqParsed);
        Assert.Equal(req.RequestId, reqParsed!.RequestId);
        Assert.Equal(req.Type, reqParsed.Type);
        Assert.Equal(req.Target, reqParsed.Target);
    }
}
