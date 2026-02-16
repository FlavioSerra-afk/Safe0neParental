using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Safe0ne.DashboardServer.ControlPlane;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.Reports;

/// <summary>
/// 16W9: Minimal SSOT-backed reports scheduler.
/// - Schedules are authored in Parent UI and stored in the Local Settings Profile (SSOT) under policy.reports.
/// - Execution state is stored under root.reportsState and must NOT bump policyVersion.
/// - When a digest runs, we append an activity event kind=report_digest so the UI can surface it.
///
/// This is intentionally small and local-first: no external delivery yet.
/// </summary>
public sealed class ReportSchedulerService : BackgroundService
{
    private readonly JsonFileControlPlane _cp;

    public ReportSchedulerService(JsonFileControlPlane cp)
    {
        _cp = cp;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start slightly delayed so the server can finish boot.
        try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); } catch { }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Tick(DateTimeOffset.UtcNow);
            }
            catch
            {
                // Best-effort scheduler; never bring down the server.
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch
            {
                // ignore
            }
        }
    }

    private void Tick(DateTimeOffset nowUtc)
    {
        var children = _cp.GetChildren();
        foreach (var c in children)
        {
            try
            {
                var childId = c.Id;
                if (childId is null) continue;
                ReportsDigest.TryRunDigestIfDue(_cp, childId, nowUtc, force: false);
            }
            catch
            {
                // best-effort
            }
        }
    }
}

public static class ReportsDigest
{
    /// <summary>
    /// Runs a digest if due (or force=true). Returns true if a digest was generated.
    /// </summary>
    public static bool TryRunDigestIfDue(JsonFileControlPlane cp, ChildId childId, DateTimeOffset nowUtc, bool force)
    {
        var profileJson = cp.GetOrCreateLocalSettingsProfileJson(childId);
        JsonObject root;
        try { root = JsonNode.Parse(profileJson) as JsonObject ?? new JsonObject(); }
        catch { root = new JsonObject(); }

        var policy = root["policy"] as JsonObject ?? new JsonObject();
        var reports = policy["reports"] as JsonObject;
        if (reports is null)
            return false;

        var enabled = reports.TryGetPropertyValue("enabled", out var en) && en is JsonValue jvEn && SafeBool(jvEn);
        var digest = reports["digest"] as JsonObject;
        if (!enabled || digest is null)
            return false;

        var frequency = SafeString(digest, "frequency", "off");
        var timeLocal = SafeString(digest, "timeLocal", "18:00");
        var weekday = SafeString(digest, "weekday", "sun");

        if (string.Equals(frequency, "off", StringComparison.OrdinalIgnoreCase))
            return false;

        var state = root["reportsState"] as JsonObject ?? new JsonObject();
        root["reportsState"] = state;

        DateTimeOffset? lastDigestAtUtc = null;
        if (state.TryGetPropertyValue("lastDigestAtUtc", out var ld) && ld is JsonValue jvLd)
        {
            try
            {
                var s = jvLd.GetValue<string?>();
                if (!string.IsNullOrWhiteSpace(s) && DateTimeOffset.TryParse(s, out var dto))
                    lastDigestAtUtc = dto;
            }
            catch { }
        }

        var mostRecentScheduledUtc = ComputeMostRecentScheduleUtc(nowUtc, frequency, timeLocal, weekday);

        if (!force)
        {
            if (mostRecentScheduledUtc is null)
                return false;

            // Already ran for this scheduled slot?
            if (lastDigestAtUtc is not null && lastDigestAtUtc.Value >= mostRecentScheduledUtc.Value)
                return false;

            // Not yet time.
            if (nowUtc < mostRecentScheduledUtc.Value)
                return false;
        }

        // Determine horizon.
        var fromUtc = lastDigestAtUtc;
        if (fromUtc is null)
        {
            fromUtc = string.Equals(frequency, "weekly", StringComparison.OrdinalIgnoreCase)
                ? nowUtc.AddDays(-7)
                : nowUtc.AddDays(-1);
        }

        var summary = BuildDigestSummary(cp, childId, fromUtc.Value, nowUtc);

        // Append activity event.
        var evt = new JsonObject
        {
            ["kind"] = "report_digest",
            ["occurredAtUtc"] = nowUtc.ToString("O"),
            ["details"] = JsonSerializer.Serialize(new
            {
                frequency,
                timeLocal,
                weekday,
                fromUtc = fromUtc.Value.ToString("O"),
                toUtc = nowUtc.ToString("O"),
                summary
            }, JsonDefaults.Options)
        };

        var arr = new JsonArray { evt };
        cp.AppendLocalActivityJson(childId, arr.ToJsonString(JsonDefaults.Options));

        // Persist execution state WITHOUT bumping policyVersion.
        state["lastDigestAtUtc"] = nowUtc.ToString("O");
        state["lastDigestSummary"] = summary;

        try
        {
            cp.UpsertLocalSettingsProfileJson(childId, root.ToJsonString(JsonDefaults.Options), bumpPolicyVersionOnPolicyChange: false);
        }
        catch
        {
            // best-effort
        }

        return true;
    }

    public static object ReadScheduleEnvelope(JsonFileControlPlane cp, ChildId childId, DateTimeOffset nowUtc)
    {
        var profileJson = cp.GetOrCreateLocalSettingsProfileJson(childId);
        JsonObject root;
        try { root = JsonNode.Parse(profileJson) as JsonObject ?? new JsonObject(); }
        catch { root = new JsonObject(); }

        var policy = root["policy"] as JsonObject ?? new JsonObject();
        var reports = policy["reports"] as JsonObject ?? new JsonObject();
        var digest = reports["digest"] as JsonObject ?? new JsonObject();

        var enabled = reports.TryGetPropertyValue("enabled", out var en) && en is JsonValue jvEn && SafeBool(jvEn);
        var frequency = SafeString(digest, "frequency", "off");
        var timeLocal = SafeString(digest, "timeLocal", "18:00");
        var weekday = SafeString(digest, "weekday", "sun");

        var state = root["reportsState"] as JsonObject ?? new JsonObject();
        var lastDigestAtUtc = state.TryGetPropertyValue("lastDigestAtUtc", out var ld) && ld is JsonValue jvLd ? (jvLd.TryGetValue<string>(out var s) ? s : null) : null;
        var lastDigestSummary = state.TryGetPropertyValue("lastDigestSummary", out var ls) && ls is JsonValue jvLs ? (jvLs.TryGetValue<string>(out var s2) ? s2 : null) : null;

        var nextDueUtc = ComputeNextScheduleUtc(nowUtc, frequency, timeLocal, weekday);

        return new
        {
            childId = childId.Value,
            schedule = new
            {
                enabled,
                digest = new { frequency, timeLocal, weekday }
            },
            state = new
            {
                lastDigestAtUtc,
                lastDigestSummary,
                nextDueAtUtc = nextDueUtc?.ToString("O")
            }
        };
    }

    public static void UpsertSchedule(JsonFileControlPlane cp, ChildId childId, JsonObject schedulePatch)
    {
        var profileJson = cp.GetOrCreateLocalSettingsProfileJson(childId);
        JsonObject root;
        try { root = JsonNode.Parse(profileJson) as JsonObject ?? new JsonObject(); }
        catch { root = new JsonObject(); }

        var policy = root["policy"] as JsonObject ?? new JsonObject();
        root["policy"] = policy;

        var reports = policy["reports"] as JsonObject ?? new JsonObject();
        policy["reports"] = reports;

        // Apply patch: { enabled?, digest?{frequency,timeLocal,weekday}}
        if (schedulePatch.TryGetPropertyValue("enabled", out var en) && en is JsonValue)
            reports["enabled"] = en.DeepClone();

        if (schedulePatch.TryGetPropertyValue("digest", out var dg) && dg is JsonObject dgObj)
        {
            var digest = reports["digest"] as JsonObject ?? new JsonObject();
            reports["digest"] = digest;
            foreach (var kv in dgObj)
            {
                digest[kv.Key] = kv.Value?.DeepClone();
            }
        }

        cp.UpsertLocalSettingsProfileJson(childId, root.ToJsonString(JsonDefaults.Options));
    }

    private static DateTimeOffset? ComputeMostRecentScheduleUtc(DateTimeOffset nowUtc, string frequency, string timeLocal, string weekday)
    {
        if (string.Equals(frequency, "daily", StringComparison.OrdinalIgnoreCase))
        {
            var local = TimeZoneInfo.ConvertTime(nowUtc, TimeZoneInfo.Local);
            if (!TryParseTime(timeLocal, out var hh, out var mm)) return null;
            var scheduledLocal = new DateTime(local.Year, local.Month, local.Day, hh, mm, 0, DateTimeKind.Unspecified);
            var scheduledUtc = TimeZoneInfo.ConvertTimeToUtc(scheduledLocal, TimeZoneInfo.Local);
            if (nowUtc >= scheduledUtc) return new DateTimeOffset(scheduledUtc, TimeSpan.Zero);
            // previous day
            scheduledLocal = scheduledLocal.AddDays(-1);
            scheduledUtc = TimeZoneInfo.ConvertTimeToUtc(scheduledLocal, TimeZoneInfo.Local);
            return new DateTimeOffset(scheduledUtc, TimeSpan.Zero);
        }

        if (string.Equals(frequency, "weekly", StringComparison.OrdinalIgnoreCase))
        {
            var local = TimeZoneInfo.ConvertTime(nowUtc, TimeZoneInfo.Local);
            if (!TryParseTime(timeLocal, out var hh, out var mm)) return null;
            var target = ParseWeekday(weekday);
            var daysBack = ((7 + (int)local.DayOfWeek - (int)target) % 7);
            var scheduledLocal = new DateTime(local.Year, local.Month, local.Day, hh, mm, 0, DateTimeKind.Unspecified).AddDays(-daysBack);
            var scheduledUtc = TimeZoneInfo.ConvertTimeToUtc(scheduledLocal, TimeZoneInfo.Local);
            // if we're earlier than this week's slot, go back one week
            if (nowUtc < scheduledUtc)
            {
                scheduledLocal = scheduledLocal.AddDays(-7);
                scheduledUtc = TimeZoneInfo.ConvertTimeToUtc(scheduledLocal, TimeZoneInfo.Local);
            }
            return new DateTimeOffset(scheduledUtc, TimeSpan.Zero);
        }

        return null;
    }

    private static DateTimeOffset? ComputeNextScheduleUtc(DateTimeOffset nowUtc, string frequency, string timeLocal, string weekday)
    {
        if (string.Equals(frequency, "off", StringComparison.OrdinalIgnoreCase)) return null;
        var recent = ComputeMostRecentScheduleUtc(nowUtc, frequency, timeLocal, weekday);
        if (recent is null) return null;
        return string.Equals(frequency, "weekly", StringComparison.OrdinalIgnoreCase)
            ? recent.Value.AddDays(7)
            : recent.Value.AddDays(1);
    }

    private static string BuildDigestSummary(JsonFileControlPlane cp, ChildId childId, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        try
        {
            var json = cp.GetLocalActivityJson(childId, fromUtc, toUtc, take: 500);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return "No activity.";

            var total = 0;
            var byKind = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                total++;
                if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String)
                {
                    var key = k.GetString() ?? "unknown";
                    byKind[key] = byKind.TryGetValue(key, out var v) ? v + 1 : 1;
                }
            }

            if (total == 0) return "No activity.";

            var top = byKind.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key}:{kv.Value}");
            return $"{total} event(s) ({string.Join(", ", top)})";
        }
        catch
        {
            return "Digest generated.";
        }
    }

    private static bool SafeBool(JsonValue v)
    {
        try { return v.GetValue<bool>(); } catch { return false; }
    }

    private static string SafeString(JsonObject o, string key, string fallback)
    {
        if (!o.TryGetPropertyValue(key, out var n) || n is null) return fallback;
        try
        {
            if (n is JsonValue jv) return jv.GetValue<string>() ?? fallback;
        }
        catch { }
        return fallback;
    }

    private static bool TryParseTime(string timeLocal, out int hh, out int mm)
    {
        hh = 18; mm = 0;
        var s = (timeLocal ?? "").Trim();
        var parts = s.Split(':');
        if (parts.Length != 2) return false;
        return int.TryParse(parts[0], out hh) && int.TryParse(parts[1], out mm) && hh >= 0 && hh <= 23 && mm >= 0 && mm <= 59;
    }

    private static DayOfWeek ParseWeekday(string weekday)
    {
        return (weekday ?? "sun").Trim().ToLowerInvariant() switch
        {
            "mon" => DayOfWeek.Monday,
            "tue" => DayOfWeek.Tuesday,
            "wed" => DayOfWeek.Wednesday,
            "thu" => DayOfWeek.Thursday,
            "fri" => DayOfWeek.Friday,
            "sat" => DayOfWeek.Saturday,
            _ => DayOfWeek.Sunday
        };
    }
}
