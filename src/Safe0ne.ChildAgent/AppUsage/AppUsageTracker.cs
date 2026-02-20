using System.Text.Json;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.AppUsage;

public sealed class AppUsageTracker
{
    private readonly ChildId _childId;
    private readonly string _path;

    public AppUsageTracker(ChildId childId)
    {
        _childId = childId;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Safe0ne",
            "ChildAgent");
        Directory.CreateDirectory(dir);

        _path = Path.Combine(dir, $"appusage.{_childId.Value}.v1.json");
    }

    public void Tick(DateTimeOffset nowUtc, bool isActive, string? foregroundProcessName)
    {
        var state = Load() ?? new State(
            LocalDate: LocalDateString(nowUtc.ToLocalTime()),
            LastTickUtc: nowUtc,
            UsedSecondsByProcess: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            BlockedCountsByProcessReason: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

        var today = LocalDateString(nowUtc.ToLocalTime());
        if (!string.Equals(state.LocalDate, today, StringComparison.Ordinal))
        {
            state = state with
            {
                LocalDate = today,
                LastTickUtc = nowUtc,
                UsedSecondsByProcess = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                BlockedCountsByProcessReason = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            };
        }

        var delta = nowUtc - state.LastTickUtc;
        var deltaSec = (int)Math.Clamp(delta.TotalSeconds, 0, 60);
        state = state with { LastTickUtc = nowUtc };

        if (isActive && !string.IsNullOrWhiteSpace(foregroundProcessName))
        {
            var key = Normalize(foregroundProcessName);
            state.UsedSecondsByProcess.TryGetValue(key, out var current);
            state.UsedSecondsByProcess[key] = current + deltaSec;
        }

        Save(state);
    }

    public void RecordBlocked(string processName, string reason)
    {
        var s = Load();
        if (s is null) return;

        var key = $"{Normalize(processName)}|{(reason ?? "blocked").Trim().ToLowerInvariant()}";
        s.BlockedCountsByProcessReason.TryGetValue(key, out var c);
        s.BlockedCountsByProcessReason[key] = c + 1;
        Save(s);
    }

    public int GetUsedSecondsTodayFor(string processName)
    {
        var s = Load();
        if (s is null) return 0;
        var key = Normalize(processName);
        return s.UsedSecondsByProcess.TryGetValue(key, out var v) ? v : 0;
    }

    public string GetLocalDateString()
    {
        var s = Load();
        if (s is not null && !string.IsNullOrWhiteSpace(s.LocalDate)) return s.LocalDate;
        return DateTimeOffset.UtcNow.ToLocalTime().ToString("yyyy-MM-dd");
    }

    public AppUsageReport BuildReport(int maxUsageItems = 8, int maxBlockedItems = 8)
    {
        var s = Load() ?? new State(
            LocalDateString(DateTimeOffset.UtcNow.ToLocalTime()),
            DateTimeOffset.UtcNow,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

        var usage = s.UsedSecondsByProcess
            .OrderByDescending(kv => kv.Value)
            .Take(maxUsageItems)
            .Select(kv => new AppUsageItem(kv.Key, kv.Value))
            .ToArray();

        // Aggregate blocked attempts per process (summing reasons)
        var byProc = new Dictionary<string, (int Count, string Reason)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in s.BlockedCountsByProcessReason)
        {
            var parts = kv.Key.Split('|', 2);
            var proc = parts[0];
            var reason = parts.Length > 1 ? parts[1] : "blocked";

            if (!byProc.TryGetValue(proc, out var v))
                byProc[proc] = (kv.Value, reason);
            else
                byProc[proc] = (v.Count + kv.Value, v.Reason);
        }

        var blocked = byProc
            .OrderByDescending(kv => kv.Value.Count)
            .Take(maxBlockedItems)
            .Select(kv => new BlockedAttemptItem(kv.Key, kv.Value.Count, kv.Value.Reason))
            .ToArray();

        return new AppUsageReport(s.LocalDate, usage, blocked);
    }

    private State? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<State>(json, JsonDefaults.Options);
        }
        catch
        {
            return null;
        }
    }

    private void Save(State s)
    {
        try
        {
            var json = JsonSerializer.Serialize(s, JsonDefaults.Options);
            File.WriteAllText(_path, json);
        }
        catch { }
    }

    private static string LocalDateString(DateTimeOffset local) => local.ToString("yyyy-MM-dd");

    private static string Normalize(string name)
    {
        var n = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (n.Length == 0) return n;
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n : (n + ".exe");
    }

    private sealed record State(
        string LocalDate,
        DateTimeOffset LastTickUtc,
        Dictionary<string, int> UsedSecondsByProcess,
        Dictionary<string, int> BlockedCountsByProcessReason);
}
