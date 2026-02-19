using System.Security.Cryptography;\nusing System.Text.Json;\nusing System.Text.Json.Nodes;\nusing Safe0ne.Shared.Contracts;\n\nnamespace Safe0ne.DashboardServer.ControlPlane;\n

// AUTO-SPLIT CP01: Children + local meta/location helpers.
// No behavior changes; purely a partial-by-domain split to reduce churn.

public sealed partial class JsonFileControlPlane
{
        public sealed record LocalChildSnapshot(
        Guid Id,
        string DisplayName,
        string? Gender,
        string? AgeGroup,
        JsonElement? Avatar,
        bool IsArchived,
        DateTimeOffset? ArchivedAtUtc)
    {
        // Compatibility aliases used by older codepaths / endpoints.
        [System.Text.Json.Serialization.JsonIgnore]
        public bool Archived => IsArchived;

        [System.Text.Json.Serialization.JsonIgnore]
        public DateTimeOffset? ArchivedAt => ArchivedAtUtc;
    }
    public IReadOnlyList<ChildProfile> GetChildren()
    {
        lock (_gate) { return _children.ToList(); }
    }
public IReadOnlyList<LocalChildSnapshot> GetChildrenWithArchiveState(bool includeArchived)
{
    lock (_gate)
    {
        IEnumerable<ChildProfile> list = _children;
        if (!includeArchived)
        {
            list = list.Where(c => !_archivedAtByChildGuid.ContainsKey(c.Id.Value.ToString()));
        }

        return list
            .Select(c =>
            {
                var key = c.Id.Value.ToString();
                _archivedAtByChildGuid.TryGetValue(key, out var archivedAt);

                string? gender = null;
                string? ageGroup = null;
                JsonElement? avatar = null;

                if (_localChildMetaJsonByChildGuid.TryGetValue(key, out var metaJson) && !string.IsNullOrWhiteSpace(metaJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(metaJson);
                        if (doc.RootElement.TryGetProperty("gender", out var g) && g.ValueKind == JsonValueKind.String)
                            gender = g.GetString();
                        if (doc.RootElement.TryGetProperty("ageGroup", out var a) && a.ValueKind == JsonValueKind.String)
                            ageGroup = a.GetString();
                        if (doc.RootElement.TryGetProperty("avatar", out var av) && av.ValueKind != JsonValueKind.Undefined && av.ValueKind != JsonValueKind.Null)
                            avatar = av.Clone();
                    }
                    catch
                    {
                        // ignore malformed meta blobs
                    }
                }

                return new LocalChildSnapshot(
                    Id: c.Id.Value,
                    DisplayName: c.DisplayName,
                    Gender: gender,
                    AgeGroup: ageGroup,
                    Avatar: avatar,
                    IsArchived: _archivedAtByChildGuid.ContainsKey(key),
                    ArchivedAtUtc: _archivedAtByChildGuid.ContainsKey(key) ? archivedAt : (DateTimeOffset?)null);
            })
            .ToList();
    }
}
    public void UpsertLocalChildMetaJson(ChildId childId, string metaJson)
    {
        var key = childId.Value.ToString();
        lock (_gate)
        {
            _localChildMetaJsonByChildGuid[key] = metaJson ?? "{}";
            PersistUnsafe_NoLock();
        }
    }
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

            take = Math.Clamp(take, 1, 1000);

            try

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


/// <summary>
/// Get the last known location JSON for a child. Returns a stable JSON object even when no location is known.
/// </summary>
public string GetLocalLocationJson(ChildId childId)
{
    var key = childId.Value.ToString();
    lock (_gate)
    {
        if (_localLastLocationJsonByChildGuid.TryGetValue(key, out var json) && !string.IsNullOrWhiteSpace(json))
        {
            // Basic retention: if capturedAtUtc exists and is older than 30 days, drop it.
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("capturedAtUtc", out var cap) &&
                    cap.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(cap.GetString(), out var ts) &&
                    ts < DateTimeOffset.UtcNow.AddDays(-30))
                {
                    _localLastLocationJsonByChildGuid.Remove(key);
                    PersistUnsafe_NoLock();
                    return DefaultLocationJson();
                }
            }
            catch
            {
                // If corrupt, treat as missing.
                _localLastLocationJsonByChildGuid.Remove(key);
                PersistUnsafe_NoLock();
                return DefaultLocationJson();
            }

            return json;
        }

}
