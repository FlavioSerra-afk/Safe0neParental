using System.Text.Json;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.Policy;

/// <summary>
/// Lightweight, privacy-first health state for policy sync.
/// Purpose: avoid retry storms and provide parent-side observability (via TamperSignals).
/// Best-effort only; failures must never crash the agent.
/// </summary>
public static class PolicySyncHealthStore
{
    public sealed record Entry(
        DateTimeOffset UpdatedAtUtc,
        int ConsecutiveHeartbeatFailures,
        int ConsecutivePolicyFetchFailures,
        DateTimeOffset? LastHeartbeatOkAtUtc,
        DateTimeOffset? LastPolicyFetchOkAtUtc,
        DateTimeOffset? AuthRejectedAtUtc);

    public static Entry Load(ChildId childId)
    {
        try
        {
            var path = PathFor(childId);
            if (!File.Exists(path))
                return new Entry(DateTimeOffset.UtcNow, 0, 0, null, null, null);
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Entry>(json, JsonDefaults.Options)
                   ?? new Entry(DateTimeOffset.UtcNow, 0, 0, null, null, null);
        }
        catch
        {
            return new Entry(DateTimeOffset.UtcNow, 0, 0, null, null, null);
        }
    }

    public static void Save(ChildId childId, Entry entry)
    {
        try
        {
            var path = PathFor(childId);
            var json = JsonSerializer.Serialize(entry with { UpdatedAtUtc = DateTimeOffset.UtcNow }, JsonDefaults.Options);
            File.WriteAllText(path, json);
        }
        catch
        {
            // best-effort
        }
    }

    private static string PathFor(ChildId childId)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Safe0ne",
            "ChildAgent");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"policy-sync-health.{childId.Value}.json");
    }
}
