using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

public sealed partial class JsonFileControlPlane
{
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

        return DefaultLocationJson();
    }
}

/// <summary>
/// Upsert last known location JSON for a child. Stored as a JSON object.
/// </summary>
public void UpsertLocalLocationJson(ChildId childId, string locationJson)
{
    var key = childId.Value.ToString();
    lock (_gate)
    {
        // Validate it's a JSON object.
        try
        {
            using var doc = JsonDocument.Parse(locationJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Location payload must be a JSON object.");
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Ignore invalid payloads.
            return;
        }

        EnsureChildProfileUnsafe_NoLock(childId);
        _localLastLocationJsonByChildGuid[key] = locationJson;
        PersistUnsafe_NoLock();
    }
}

private static string DefaultLocationJson()
    => @"{""available"":false,""capturedAtUtc"":null,""latitude"":null,""longitude"":null,""accuracyMeters"":null,""source"":null,""note"":null}";

                


public ChildProfile CreateChild(string displayName)
{
    if (string.IsNullOrWhiteSpace(displayName))
        throw new ArgumentException("DisplayName is required.", nameof(displayName));

    lock (_gate)
    {
        var id = new ChildId(Guid.NewGuid());
        var profile = new ChildProfile(id, displayName.Trim());
        _children.Add(profile);

        // Seed policy if missing.
        if (!_policiesByChildGuid.ContainsKey(id.Value.ToString()))
        {
            _policiesByChildGuid[id.Value.ToString()] = new ChildPolicy(
                ChildId: id,
                Version: new PolicyVersion(1),
                Mode: SafetyMode.Open,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedBy: "local");
        }

        PersistUnsafe_NoLock();
        return profile;
    }
}

public ChildProfile UpdateChild(ChildId childId, string? displayName, bool? archived)
{
    lock (_gate)
    {
        var idx = _children.FindIndex(c => c.Id.Value == childId.Value);
        if (idx < 0)
        {
            throw new InvalidOperationException("Child not found.");
        }

        var current = _children[idx];
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            current = current with { DisplayName = displayName.Trim() };
            _children[idx] = current;
        }

        var key = childId.Value.ToString();
        if (archived is not null)
        {
            if (archived.Value)
            {
                _archivedAtByChildGuid[key] = DateTimeOffset.UtcNow;
            }
            else
            {
                _archivedAtByChildGuid.Remove(key);
            }
        }

        PersistUnsafe_NoLock();
        return current;
    }
}

public bool IsArchived(ChildId childId)
{
    lock (_gate)
    {
        return _archivedAtByChildGuid.ContainsKey(childId.Value.ToString());
    }
}

}
