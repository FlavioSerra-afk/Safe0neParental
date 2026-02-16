using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.ChildUx;

internal static class ScheduleHelper
{
    public static NextScheduleChange? GetNextChange(DateTimeOffset nowLocal, ChildPolicy? policy)
    {
        if (policy is null) return null;

        var candidates = new List<(DateTimeOffset When, string Name)>();
        AddWindow(candidates, nowLocal, "Bedtime", policy.BedtimeWindow);
        AddWindow(candidates, nowLocal, "School", policy.SchoolWindow);
        AddWindow(candidates, nowLocal, "Homework", policy.HomeworkWindow);

        if (candidates.Count == 0) return null;

        var next = candidates.OrderBy(c => c.When).First();
        return new NextScheduleChange(next.Name, next.When);
    }

    private static void AddWindow(List<(DateTimeOffset When, string Name)> list, DateTimeOffset nowLocal, string name, ScheduleWindow? w)
    {
        if (w is null || !w.Enabled) return;
        if (!TryParseLocalTime(w.StartLocal, out var start) || !TryParseLocalTime(w.EndLocal, out var end)) return;

        // Candidate boundary times for today/tomorrow.
        var baseDate = nowLocal.Date;
        var startToday = new DateTimeOffset(baseDate.Add(start), nowLocal.Offset);
        var endToday = new DateTimeOffset(baseDate.Add(end), nowLocal.Offset);

        // If the window crosses midnight (e.g. 22:00 -> 07:00), end is tomorrow.
        if (end <= start)
        {
            endToday = endToday.AddDays(1);
        }

        // Consider both the current day's boundaries, and the next day's start.
        var startNext = startToday.AddDays(1);
        var endNext = endToday.AddDays(1);

        AddIfFuture(list, name, startToday, nowLocal);
        AddIfFuture(list, name, endToday, nowLocal);
        AddIfFuture(list, name, startNext, nowLocal);
        AddIfFuture(list, name, endNext, nowLocal);
    }

    private static void AddIfFuture(List<(DateTimeOffset When, string Name)> list, string name, DateTimeOffset candidate, DateTimeOffset nowLocal)
    {
        if (candidate > nowLocal)
            list.Add((candidate, name));
    }

    private static bool TryParseLocalTime(string raw, out TimeSpan t)
    {
        t = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return TimeSpan.TryParse(raw.Trim(), out t);
    }
}

public sealed record NextScheduleChange(
    string Name,
    DateTimeOffset WhenLocal);
