using System.Text.Json;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

public sealed partial class JsonFileControlPlane
{
    // Local Mode Activity: append/query child activity events.
        // Activity is stored as a JSON array per child for flexibility and forward-compat.
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

                    // Retention: keep only the newest N events and prune >30 days by occurredAtUtc if present.
                    var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
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

                    // Keep last 2000
                    if (filtered.Count > 2000)
                        filtered = filtered.Skip(filtered.Count - 2000).ToList();

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

                // Retention currently keeps up to 2000 events; allow callers to request up to that.
                take = Math.Clamp(take, 1, 2000);

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


    // Newest first
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
}
