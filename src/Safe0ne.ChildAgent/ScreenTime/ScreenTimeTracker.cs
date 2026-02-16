using System.Text.Json;
using System.Linq;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.ScreenTime;

/// <summary>
/// K4: Privacy-first daily screen time tracker.
/// - Counts only “active” time (idle-filtered).
/// - Stores a tiny per-child local JSON state under LocalAppData.
/// </summary>
public sealed class ScreenTimeTracker
{
    private readonly ChildId _childId;
    private readonly string _path;

    public ScreenTimeTracker(ChildId childId)
    {
        _childId = childId;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Safe0ne",
            "ChildAgent");
        Directory.CreateDirectory(dir);

        _path = Path.Combine(dir, $"screentime.{_childId.Value}.v1.json");
    }

    public ScreenTimeTickResult Tick(DateTimeOffset nowUtc, TimeSpan idleTime, TimeSpan idleThreshold)
    {
        var state = Load() ?? new State(
            LocalDate: LocalDateString(nowUtc.ToLocalTime()),
            UsedSecondsToday: 0,
            LastTickUtc: nowUtc,
            Warned5: false,
            Warned1: false,
            WarnedAtMinutesRemaining: Array.Empty<int>());

        var today = LocalDateString(nowUtc.ToLocalTime());
        if (!string.Equals(state.LocalDate, today, StringComparison.Ordinal))
        {
            state = state with { LocalDate = today, UsedSecondsToday = 0, Warned5 = false, Warned1 = false, WarnedAtMinutesRemaining = Array.Empty<int>(), LastTickUtc = nowUtc };
        }

        var delta = nowUtc - state.LastTickUtc;
        var deltaSec = (int)Math.Clamp(delta.TotalSeconds, 0, 60); // cap avoids spikes if the machine sleeps
        var count = idleTime <= idleThreshold;

        var used = state.UsedSecondsToday + (count ? deltaSec : 0);
        state = state with { UsedSecondsToday = used, LastTickUtc = nowUtc };

        Save(state);
        return new ScreenTimeTickResult(state.LocalDate, state.UsedSecondsToday, state.Warned5, state.Warned1, state.WarnedAtMinutesRemaining ?? Array.Empty<int>());
    }

    public bool HasWarnedAtMinutesRemaining(int minutes)
    {
        var s = Load();
        if (s is null) return false;

        // Legacy compatibility: older state used explicit flags.
        if (minutes == 5 && s.Warned5) return true;
        if (minutes == 1 && s.Warned1) return true;

        var arr = s.WarnedAtMinutesRemaining ?? Array.Empty<int>();
        return Array.IndexOf(arr, minutes) >= 0;
    }

    public void MarkWarnedAtMinutesRemaining(int minutes)
    {
        if (minutes <= 0) return;
        var s = Load();
        if (s is null) return;

        if (minutes == 5 && s.Warned5) return;
        if (minutes == 1 && s.Warned1) return;

        var arr = s.WarnedAtMinutesRemaining ?? Array.Empty<int>();
        if (Array.IndexOf(arr, minutes) >= 0) return;

        var next = arr.Concat(new[] { minutes }).Distinct().OrderByDescending(x => x).ToArray();

        // Keep legacy flags in sync for 5/1 (marker-safe).
        var warned5 = s.Warned5 || minutes == 5;
        var warned1 = s.Warned1 || minutes == 1;

        Save(s with { Warned5 = warned5, Warned1 = warned1, WarnedAtMinutesRemaining = next });
    }

    // Convenience legacy helpers retained for older call sites.
    public void MarkWarned5() => MarkWarnedAtMinutesRemaining(5);
    public void MarkWarned1() => MarkWarnedAtMinutesRemaining(1);

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
        catch
        {
            // ignore (best-effort)
        }
    }

    private static string LocalDateString(DateTimeOffset local)
        => local.ToString("yyyy-MM-dd");

    private sealed record State(
        string LocalDate,
        int UsedSecondsToday,
        DateTimeOffset LastTickUtc,
        bool Warned5,
        bool Warned1,
        int[]? WarnedAtMinutesRemaining);

    public sealed record ScreenTimeTickResult(
        string LocalDate,
        int UsedSecondsToday,
        bool Warned5,
        bool Warned1,
        int[] WarnedAtMinutesRemaining);
}
