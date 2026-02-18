using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// Activity domain partial (Local Mode).
/// Activity is stored as a JSON array per child for forward-compat and to minimize contract churn.
/// </summary>
public sealed partial class JsonFileControlPlane
{
    /// <summary>
    /// Retention window for Local Mode activity (days). Kept conservative to avoid SSOT bloat.
    /// </summary>
    internal const int LocalActivityRetentionDays = 30;

    /// <summary>
    /// Maximum number of activity events retained per child (newest kept).
    /// </summary>
    internal const int LocalActivityMaxEvents = 2000;

    // Local Mode Activity: append/query child activity events.

    public void AppendLocalActivityJson(ChildId childId, string activityEventsJsonArray)
    {
        var key = childId.Value.ToString();
        lock (_gate)
        {
            AppendLocalActivityJsonUnsafe_NoLock(key, activityEventsJsonArray);
        }
    }

    private void AppendLocalActivityJsonUnsafe_NoLock(string key, string activityEventsJsonArray)
    {
        // Normalize: treat null/empty as no-op.
        if (string.IsNullOrWhiteSpace(activityEventsJsonArray))
            return;

        // Load existing array.
        var existing = _localActivityEventsJsonByChildGuid.TryGetValue(key, out var current) && !string.IsNullOrWhiteSpace(current)
            ? current
            : "[]";

        try
        {
            using var existingDoc = JsonDocument.Parse(existing);
            using var incomingDoc = JsonDocument.Parse(activityEventsJsonArray);

            if (existingDoc.RootElement.ValueKind != JsonValueKind.Array || incomingDoc.RootElement.ValueKind != JsonValueKind.Array)
                return;

            var list = new List<JsonElement>();
            foreach (var e in existingDoc.RootElement.EnumerateArray()) list.Add(e.Clone());
            foreach (var e in incomingDoc.RootElement.EnumerateArray()) list.Add(e.Clone());

            // Retention: keep only the newest N events and prune by occurredAtUtc when present.
            var cutoff = DateTimeOffset.UtcNow.AddDays(-LocalActivityRetentionDays);
            var filtered = new List<JsonElement>(list.Count);
            foreach (var e in list)
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (e.TryGetProperty("occurredAtUtc", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(t.GetString(), out var dto) && dto < cutoff)
                        continue;
                }
                filtered.Add(e);
            }

            // Keep last N (newest kept).
            if (filtered.Count > LocalActivityMaxEvents)
                filtered = filtered.Skip(filtered.Count - LocalActivityMaxEvents).ToList();

            _localActivityEventsJsonByChildGuid[key] = JsonSerializer.Serialize(filtered, JsonDefaults.Options);
            PersistUnsafe_NoLock();
        }
        catch
        {
            // Ignore malformed JSON batches.
        }
    }

    public string GetLocalActivityJson(ChildId childId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int take)
    {
        var key = childId.Value.ToString();
        lock (_gate)
        {
            if (!_localActivityEventsJsonByChildGuid.TryGetValue(key, out var json) || string.IsNullOrWhiteSpace(json))
                return "[]";

            take = Math.Clamp(take, 1, 1000);

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return "[]";

                var list = new List<JsonElement>();
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    DateTimeOffset occurred = DateTimeOffset.MinValue;
                    if (e.TryGetProperty("occurredAtUtc", out var t) && t.ValueKind == JsonValueKind.String)
                        DateTimeOffset.TryParse(t.GetString(), out occurred);

                    if (fromUtc is not null && occurred != DateTimeOffset.MinValue && occurred < fromUtc.Value) continue;
                    if (toUtc is not null && occurred != DateTimeOffset.MinValue && occurred > toUtc.Value) continue;
                    list.Add(e.Clone());
                }

                // Newest first.
                list = list.OrderByDescending(e =>
                {
                    if (e.TryGetProperty("occurredAtUtc", out var t) && t.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(t.GetString(), out var dto))
                        return dto;
                    return DateTimeOffset.MinValue;
                }).Take(take).ToList();

                return JsonSerializer.Serialize(list, JsonDefaults.Options);
            }
            catch
            {
                return "[]";
            }
        }
    }

    /// <summary>
    /// Export all retained activity for a child as a stable envelope.
    /// This is an export stub for now (JSON-only) and is safe for later bundling into a diagnostics ZIP.
    /// </summary>
    public string ExportLocalActivityJsonEnvelope(ChildId childId)
    {
        var key = childId.Value.ToString();
        lock (_gate)
        {
            var json = _localActivityEventsJsonByChildGuid.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)
                ? raw
                : "[]";

            JsonNode? node = null;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch { node = new JsonArray(); }

            var env = new JsonObject
            {
                ["childId"] = childId.Value.ToString(),
                ["exportedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["retentionDays"] = LocalActivityRetentionDays,
                ["maxEvents"] = LocalActivityMaxEvents,
                ["events"] = node ?? new JsonArray()
            };

            return env.ToJsonString(JsonDefaults.Options);
        }
    }
}
