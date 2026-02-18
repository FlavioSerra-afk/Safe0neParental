using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// File-backed control plane store.
/// Stores a small JSON snapshot under the current user's LocalApplicationData.
/// </summary>
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

private const int CurrentSchemaVersion = 2;
    private const int MinSupportedSchemaVersion = 1;


private readonly object _gate = new();
    private readonly string _path;

    private List<ChildProfile> _children = new();
    private Dictionary<string, DateTimeOffset> _archivedAtByChildGuid = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ChildPolicy> _policiesByChildGuid = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ChildAgentStatus> _statusByChildGuid = new(StringComparer.OrdinalIgnoreCase);

    
    // Local mode: UI metadata + settings profile blobs (stored as JSON).
    private Dictionary<string, string> _localChildMetaJsonByChildGuid = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _localSettingsProfileJsonByChildGuid = new(StringComparer.OrdinalIgnoreCase);

    // Local mode: activity events (stored as JSON arrays per child).
    private Dictionary<string, string> _localActivityEventsJsonByChildGuid = new(StringComparer.OrdinalIgnoreCase);

    // Local mode: last known location (stored as JSON object per child).
    private Dictionary<string, string> _localLastLocationJsonByChildGuid = new(StringComparer.OrdinalIgnoreCase);

// K1: per-child paired devices and pending pairing codes.
    private Dictionary<string, List<PairedDevice>> _devicesByChildGuid = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, PendingPairing> _pendingPairingByChildGuid = new(StringComparer.OrdinalIgnoreCase);

    // K2: queued commands (control plane -> agent).
    private Dictionary<string, List<ChildCommand>> _commandsByChildGuid = new(StringComparer.OrdinalIgnoreCase);

    // K8/P11: access requests + grants.
    private List<AccessRequest> _requests = new();
    private List<Grant> _grants = new();

    // K9: diagnostics bundles (latest per child).
    private Dictionary<string, DiagnosticsBundleInfo> _diagnosticsByChildGuid = new(StringComparer.OrdinalIgnoreCase);

    public JsonFileControlPlane()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Safe0ne",
            "DashboardServer");

        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "control-plane.v1.json");

        LoadOrSeed();
    }


    public (bool ok, string? error) TryHealthCheck()
    {
        lock (_gate)
        {
            try
            {
                // Ensure current in-memory state is loadable and the directory is writable.
                var dir = Path.GetDirectoryName(_path) ?? string.Empty;
                Directory.CreateDirectory(dir);

                var probe = Path.Combine(dir, "health.probe");
                File.WriteAllText(probe, DateTimeOffset.UtcNow.ToString("O"));
                File.Delete(probe);

                // Read touch: should not throw.
                _ = _children.Count;
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }

    public object GetInfo()
    {
        lock (_gate)
        {
            return new
            {
                ok = true,
                backend = "json",
                schema = "control-plane.v1.json",
                schemaVersion = CurrentSchemaVersion,
                storagePath = _path,
                childrenCount = _children.Count,
                requestsCount = _requests.Count,
                atUtc = DateTimeOffset.UtcNow
            };
        }
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


    // Local Mode helpers (Parent UI profile/settings blobs)

    // NOTE: Local settings profiles are JSON blobs for forward-compat.
    // Provide additive defaults + auto-migration so older profiles remain valid when new policy fields are introduced.

    private static System.Text.Json.Nodes.JsonObject BuildDefaultLocalSettingsProfile(string childKey)
    {
        // Keep legacy keys (permissions/limits) for backward compatibility, even as the policy surface expands.
        return new System.Text.Json.Nodes.JsonObject
        {
            ["childId"] = childKey,
            ["policyVersion"] = 1,
            ["effectiveAtUtc"] = null,
            ["policy"] = new System.Text.Json.Nodes.JsonObject
            {
                ["mode"] = "Open",
                ["timeBudget"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["dailyMinutes"] = 120,
                    ["warnMinutesRemaining"] = new System.Text.Json.Nodes.JsonArray(5, 1),
                    ["schedules"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["bedtime"] = new System.Text.Json.Nodes.JsonObject
                        {
                            ["enabled"] = false,
                            ["startLocal"] = "21:00",
                            ["endLocal"] = "07:00"
                        },
                        ["school"] = new System.Text.Json.Nodes.JsonObject
                        {
                            ["enabled"] = false,
                            ["startLocal"] = "09:00",
                            ["endLocal"] = "15:00"
                        },
                        ["homework"] = new System.Text.Json.Nodes.JsonObject
                        {
                            ["enabled"] = false,
                            ["startLocal"] = "16:00",
                            ["endLocal"] = "18:00"
                        }
                    }
                },
                ["routines"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["bedtimeTemplate"] = "default",
                    ["schoolTemplate"] = "default"
                },
                ["apps"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["allowList"] = new System.Text.Json.Nodes.JsonArray(),
                    ["denyList"] = new System.Text.Json.Nodes.JsonArray(),
                    ["perAppLimits"] = new System.Text.Json.Nodes.JsonArray(),
                    ["blockNewApps"] = false
                },
                ["web"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["categoryRules"] = new System.Text.Json.Nodes.JsonArray(),
                    ["safeSearch"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["enabled"] = false,
                        ["provider"] = "stub"
                    },
                    ["allowDomains"] = new System.Text.Json.Nodes.JsonArray(),
                    ["denyDomains"] = new System.Text.Json.Nodes.JsonArray(),
                    ["adultBlockEnabled"] = false
                },
                ["exceptions"] = new System.Text.Json.Nodes.JsonArray(),
                ["alwaysAllowed"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["apps"] = new System.Text.Json.Nodes.JsonArray(),
                    ["sites"] = new System.Text.Json.Nodes.JsonArray(),
                    ["contacts"] = new System.Text.Json.Nodes.JsonArray()
                },
                ["location"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["sharingEnabled"] = false,
                    ["geofences"] = new System.Text.Json.Nodes.JsonArray()
                },
                ["alerts"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["enabled"] = true,
                    ["thresholds"] = new System.Text.Json.Nodes.JsonObject(),
                    // 16V: routing config (additive). Used by Parent UI to control where alerts surface.
                    ["routing"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["inboxEnabled"] = true,
                        ["notifyEnabled"] = false
                    }
                }
            },
            ["permissions"] = new System.Text.Json.Nodes.JsonObject
            {
                ["web"] = true,
                ["apps"] = true,
                ["bedtime"] = true,
                ["location"] = false,
                ["purchases"] = false
            },
            ["limits"] = new System.Text.Json.Nodes.JsonObject
            {
                ["screenMinutesPerDay"] = 120,
                ["bedtimeStart"] = "21:00",
                ["bedtimeEnd"] = "07:00"
            },
            ["devices"] = new System.Text.Json.Nodes.JsonArray(),

            // UI state is SSOT-backed (inside the same Local Settings Profile) so WebView storage quirks
            // cannot cause state loss. This must never bump policyVersion.
            ["ui"] = new System.Text.Json.Nodes.JsonObject
            {
                ["alerts"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["showAcknowledged"] = false,
                    ["search"] = "",
                    ["severity"] = "all",
                    ["ackedKeys"] = new System.Text.Json.Nodes.JsonArray()
                }
            }
        };
    }

    private static void MergeDefaults(System.Text.Json.Nodes.JsonNode target, System.Text.Json.Nodes.JsonNode defaults)
    {
        if (target is System.Text.Json.Nodes.JsonObject to && defaults is System.Text.Json.Nodes.JsonObject de)
        {
            foreach (var kv in de)
            {
                var key = kv.Key;
                var dv = kv.Value;
                if (!to.TryGetPropertyValue(key, out var tv) || tv is null)
                {
                    to[key] = dv?.DeepClone();
                    continue;
                }

                if (tv is System.Text.Json.Nodes.JsonObject && dv is System.Text.Json.Nodes.JsonObject)
                {
                    MergeDefaults(tv, dv);
                }
                else if (tv is System.Text.Json.Nodes.JsonArray)
                {
                    // Arrays are replace-by-source; keep existing.
                    continue;
                }
                else
                {
                    // Primitive: keep existing.
                    continue;
                }
            }
        }
    }

    private static string NormalizeLocalSettingsProfileJson(string childKey, string? profileJson)
    {
        var raw = string.IsNullOrWhiteSpace(profileJson) ? "{}" : profileJson;
        System.Text.Json.Nodes.JsonNode? parsed;
        try
        {
            parsed = System.Text.Json.Nodes.JsonNode.Parse(raw);
        }
        catch
        {
            parsed = new System.Text.Json.Nodes.JsonObject();
        }

        if (parsed is not System.Text.Json.Nodes.JsonObject obj)
        {
            obj = new System.Text.Json.Nodes.JsonObject();
        }

        // Capture whether certain new fields were present, so we can optionally map legacy values into them.
        bool hadPolicyVersion = obj["policyVersion"] is not null;
        bool hadPolicyTimeBudgetDaily = obj["policy"] is System.Text.Json.Nodes.JsonObject pol && pol["timeBudget"] is System.Text.Json.Nodes.JsonObject tb && tb["dailyMinutes"] is not null;

        // Apply additive defaults.
        var def = BuildDefaultLocalSettingsProfile(childKey);
        MergeDefaults(obj, def);

        // Ensure childId is correct.
        obj["childId"] = childKey;

        // Legacy mapping: if the new timeBudget.dailyMinutes was absent but legacy limits.screenMinutesPerDay exists, copy it over.
        try
        {
            if (!hadPolicyTimeBudgetDaily)
            {
                var lim = obj["limits"] as System.Text.Json.Nodes.JsonObject;
                var pol2 = obj["policy"] as System.Text.Json.Nodes.JsonObject;
                var tb2 = pol2?["timeBudget"] as System.Text.Json.Nodes.JsonObject;
                if (lim is not null && pol2 is not null && tb2 is not null)
                {
                    var sm = lim["screenMinutesPerDay"];
                    if (sm is not null)
                    {
                        tb2["dailyMinutes"] = sm.DeepClone();
                    }
                }
            }

            // Legacy bedtime mapping: if schedules.bedtime was absent, map permissions+limits.
            var polObj = obj["policy"] as System.Text.Json.Nodes.JsonObject;
            var tbObj = polObj?["timeBudget"] as System.Text.Json.Nodes.JsonObject;
            var sch = tbObj?["schedules"] as System.Text.Json.Nodes.JsonObject;
            var bedtime = sch?["bedtime"] as System.Text.Json.Nodes.JsonObject;
            if (bedtime is not null)
            {
                var perms = obj["permissions"] as System.Text.Json.Nodes.JsonObject;
                var lim = obj["limits"] as System.Text.Json.Nodes.JsonObject;
                if (perms is not null && perms["bedtime"] is not null)
                {
                    bedtime["enabled"] = perms["bedtime"]!.DeepClone();
                }
                if (lim is not null)
                {
                    if (lim["bedtimeStart"] is not null) bedtime["startLocal"] = lim["bedtimeStart"]!.DeepClone();
                    if (lim["bedtimeEnd"] is not null) bedtime["endLocal"] = lim["bedtimeEnd"]!.DeepClone();
                }
            }

            // If policyVersion was missing, ensure it starts at 1.
            if (!hadPolicyVersion)
            {
                obj["policyVersion"] = 1;
            }
        }
        catch
        {
            // best-effort only
        }

        return obj.ToJsonString(new System.Text.Json.JsonSerializerOptions(JsonDefaults.Options) { WriteIndented = false });
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

    public string GetOrCreateLocalSettingsProfileJson(ChildId childId)
    {
        var key = childId.Value.ToString();
        lock (_gate)
        {
            if (_localSettingsProfileJsonByChildGuid.TryGetValue(key, out var json) && !string.IsNullOrWhiteSpace(json))
            {
                var norm = NormalizeLocalSettingsProfileJson(key, json);
                if (!string.Equals(norm, json, StringComparison.Ordinal))
                {
                    _localSettingsProfileJsonByChildGuid[key] = norm;
                    PersistUnsafe_NoLock();
                }
                return norm;
            }
            // Default shape is additive-only and must remain backward compatible.
            // It must match Parent UI expectations and Kid's Local Mode profile mapping.
            var def = BuildDefaultLocalSettingsProfile(key);

            json = def.ToJsonString(JsonDefaults.Options);
            json = NormalizeLocalSettingsProfileJson(key, json);
            _localSettingsProfileJsonByChildGuid[key] = json;
            PersistUnsafe_NoLock();
            return json;
        }
    }

    public void UpsertLocalSettingsProfileJson(ChildId childId, string profileJson, bool bumpPolicyVersionOnPolicyChange = true)
    {
        var key = childId.Value.ToString();
        lock (_gate)
        {
            var incoming = NormalizeLocalSettingsProfileJson(key, profileJson ?? "{}");

            if (!bumpPolicyVersionOnPolicyChange)
            {
                _localSettingsProfileJsonByChildGuid[key] = incoming;
                PersistUnsafe_NoLock();
                return;
            }

            var existing = _localSettingsProfileJsonByChildGuid.TryGetValue(key, out var cur) && !string.IsNullOrWhiteSpace(cur)
                ? cur
                : null;

            // Policy versioning is additive-only and must remain monotonic.
            // We bump policyVersion ONLY when the policy surface changes ("policy" object).
            // Other profile writes (e.g., geofence state, diagnostics) should opt out using bumpPolicyVersionOnPolicyChange: false.

            static int ReadPolicyVersion(JsonElement root)
            {
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("policyVersion", out var pv) && pv.ValueKind == JsonValueKind.Number && pv.TryGetInt32(out var i))
                    return i;
                return 1;
            }

            static string? ReadEffectiveAtUtc(JsonElement root)
            {
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("effectiveAtUtc", out var ea) && ea.ValueKind == JsonValueKind.String)
                    return ea.GetString();
                return null;
            }

            static string ReadPolicyRaw(JsonElement root)
            {
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("policy", out var p) && p.ValueKind == JsonValueKind.Object)
                    return p.GetRawText();
                return "{}";
            }

            bool policyChanged = false;
            int existingVersion = 1;
            string? existingEffectiveAt = null;

            if (existing is not null)
            {
                try
                {
                    using var curDoc = JsonDocument.Parse(existing);
                    existingVersion = ReadPolicyVersion(curDoc.RootElement);
                    existingEffectiveAt = ReadEffectiveAtUtc(curDoc.RootElement);
                    var curPolicy = ReadPolicyRaw(curDoc.RootElement);

                    using var incDoc = JsonDocument.Parse(incoming);
                    var incPolicy = ReadPolicyRaw(incDoc.RootElement);
                    policyChanged = !string.Equals(curPolicy, incPolicy, StringComparison.Ordinal);
                }
                catch
                {
                    // If we can't parse, fall back to a conservative bump.
                    policyChanged = true;
                }
            }
            else
            {
                // First write: do not bump unless incoming already has a version.
                policyChanged = false;
            }

            // Normalize monotonicity + bump on policy changes.
            try
            {
                using var incDoc = JsonDocument.Parse(incoming);
                var incomingVersion = ReadPolicyVersion(incDoc.RootElement);

                // If policy didn't change, enforce monotonic non-decreasing version.
                if (!policyChanged && existing is not null && incomingVersion < existingVersion)
                {
                    var node = JsonNode.Parse(incoming) as JsonObject ?? new JsonObject();
                    node["policyVersion"] = existingVersion;
                    if (existingEffectiveAt is not null && (!node.TryGetPropertyValue("effectiveAtUtc", out var ea) || ea is null))
                        node["effectiveAtUtc"] = existingEffectiveAt;
                    incoming = NormalizeLocalSettingsProfileJson(key, node.ToJsonString(JsonDefaults.Options));
                }
                else if (policyChanged)
                {
                    var node = JsonNode.Parse(incoming) as JsonObject ?? new JsonObject();

                    // Bump from the existing version when possible; otherwise bump from incoming.
                    var newVersion = existing is null
                        ? Math.Max(1, incomingVersion)
                        : Math.Max(existingVersion + 1, incomingVersion);

                    node["policyVersion"] = newVersion;
                    node["effectiveAtUtc"] = DateTimeOffset.UtcNow.ToString("O");

                    incoming = NormalizeLocalSettingsProfileJson(key, node.ToJsonString(JsonDefaults.Options));
                }
            }
            catch
            {
                // If we fail to parse/mutate, store the normalized incoming to avoid breaking the flow.
            }

            _localSettingsProfileJsonByChildGuid[key] = incoming;
            PersistUnsafe_NoLock();
        }
    }

    // Local Mode Activity lives in JsonFileControlPlane.Activity.cs (domain partial).
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

public bool TryCompletePairingByCode(PairingCompleteRequest req, out PairingCompleteResponse resp)
{
    lock (_gate)
    {
        CleanupExpiredPairingsUnsafe_NoLock(DateTimeOffset.UtcNow);

        // Basic input hygiene.
        if (string.IsNullOrWhiteSpace(req.PairingCode))
        {
            resp = default!;
            return false;
        }

        // Find any pending pairing with this code.
        var pending = _pendingPairingByChildGuid.Values.FirstOrDefault(p =>
            string.Equals(p.PairingCode, req.PairingCode, StringComparison.Ordinal));

        if (pending is null)
        {
            resp = default!;
            return false;
        }

        resp = CompletePairing(pending.ChildId, req);
        return true;
    }
}

public bool TryUnpairDevice(Guid deviceId, out ChildId childId)
{
    lock (_gate)
    {
        childId = default;
        foreach (var kvp in _devicesByChildGuid)
        {
            var devices = kvp.Value;
            var idx = devices.FindIndex(d => d.DeviceId == deviceId);
            if (idx < 0) continue;

            devices.RemoveAt(idx);
            childId = new ChildId(Guid.Parse(kvp.Key));
            PersistUnsafe_NoLock();
            return true;
        }
        return false;
    }
}

public bool TryGetPendingPairing(ChildId childId, out PairingStartResponse resp)
{
    lock (_gate)
    {
        CleanupExpiredPairingsUnsafe_NoLock(DateTimeOffset.UtcNow);

        var key = childId.Value.ToString();
        if (!_pendingPairingByChildGuid.TryGetValue(key, out var pending))
        {
            resp = default!;
            return false;
        }

        resp = new PairingStartResponse(childId, pending.PairingCode, pending.ExpiresAtUtc);
        return true;
    }
}


    public bool TryGetPolicy(ChildId childId, out ChildPolicy policy)
    {
        lock (_gate)
        {
            return _policiesByChildGuid.TryGetValue(childId.Value.ToString(), out policy!);
        }
    }

    public bool TryGetStatus(ChildId childId, out ChildAgentStatus status)
    {
        lock (_gate)
        {
            return _statusByChildGuid.TryGetValue(childId.Value.ToString(), out status!);
        }
    }

    public IReadOnlyList<ChildDeviceSummary> GetDevices(ChildId childId)
    {
        lock (_gate)
        {
            var key = childId.Value.ToString();
            if (!_devicesByChildGuid.TryGetValue(key, out var devices))
            {
                return Array.Empty<ChildDeviceSummary>();
            }

            DateTimeOffset? lastSeen = null;
            if (_statusByChildGuid.TryGetValue(key, out var s))
            {
                lastSeen = s.LastSeenUtc;
            }

            return devices
                .Select(d => new ChildDeviceSummary(
                    DeviceId: d.DeviceId,
                    DeviceName: d.DeviceName,
                    AgentVersion: d.AgentVersion,
                    PairedAtUtc: d.PairedAtUtc,
                    LastSeenUtc: lastSeen))
                .ToList();
        }
    }

    public bool HasPairedDevices(ChildId childId)
    {
        lock (_gate)
        {
            var key = childId.Value.ToString();
            return _devicesByChildGuid.TryGetValue(key, out var devices) && devices.Count > 0;
        }
    }

    public bool TryValidateDeviceToken(ChildId childId, string token, out Guid deviceId)
    {
        lock (_gate)
        {
            deviceId = default;
            var key = childId.Value.ToString();
            if (!_devicesByChildGuid.TryGetValue(key, out var devices) || devices.Count == 0)
            {
                return false;
            }

            var hash = ComputeSha256Hex(token);
            var match = devices.FirstOrDefault(d => string.Equals(d.TokenHashSha256, hash, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return false;
            }

            deviceId = match.DeviceId;
            return true;
        }
    }

    public PairingStartResponse StartPairing(ChildId childId)
    {
        lock (_gate)
        {
            var key = childId.Value.ToString();
            CleanupExpiredPairingsUnsafe_NoLock(DateTimeOffset.UtcNow);

            // Ensure code uniqueness across all currently pending sessions.
            string code;
            int attempts = 0;
            do
            {
                attempts++;
                code = GenerateNumericCode(6);

                // Extremely unlikely, but guard against pathological RNG / collisions.
                if (attempts > 25)
                {
                    code = GenerateNumericCode(8);
                    break;
                }
            } while (_pendingPairingByChildGuid.Values.Any(p => string.Equals(p.PairingCode, code, StringComparison.Ordinal)));

            var expires = DateTimeOffset.UtcNow.AddMinutes(10);

            _pendingPairingByChildGuid[key] = new PendingPairing(childId, code, expires);

            EnsureChildProfileUnsafe_NoLock(childId);
            PersistUnsafe_NoLock();

            return new PairingStartResponse(childId, code, expires);
        }
    }

    private void CleanupExpiredPairingsUnsafe_NoLock(DateTimeOffset nowUtc)
    {
        // Remove any expired pairing sessions to avoid stale codes lingering.
        // Caller must hold _gate.
        if (_pendingPairingByChildGuid.Count == 0)
        {
            return;
        }

        var expiredKeys = _pendingPairingByChildGuid
            .Where(kvp => kvp.Value.ExpiresAtUtc < nowUtc)
            .Select(kvp => kvp.Key)
            .ToList();

        if (expiredKeys.Count == 0)
        {
            return;
        }

        foreach (var k in expiredKeys)
        {
            _pendingPairingByChildGuid.Remove(k);
        }

        PersistUnsafe_NoLock();
    }

    public PairingCompleteResponse CompletePairing(ChildId childId, PairingCompleteRequest req)
    {
        lock (_gate)
        {
            var key = childId.Value.ToString();
            if (!_pendingPairingByChildGuid.TryGetValue(key, out var pending))
            {
                throw new InvalidOperationException("No pending pairing session.");
            }

            if (pending.ExpiresAtUtc < DateTimeOffset.UtcNow)
            {
                _pendingPairingByChildGuid.Remove(key);
                PersistUnsafe_NoLock();
                throw new InvalidOperationException("Pairing code expired.");
            }

            if (!string.Equals(pending.PairingCode, req.PairingCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Invalid pairing code.");
            }

            // Validate and normalize device info.
            var deviceName = (req.DeviceName ?? string.Empty).Trim();
            if (deviceName.Length == 0)
            {
                deviceName = "Kid Device";
            }
            if (deviceName.Length > 64)
            {
                deviceName = deviceName[..64];
            }

            var agentVersion = (req.AgentVersion ?? string.Empty).Trim();
            if (agentVersion.Length == 0)
            {
                agentVersion = "unknown";
            }
            if (agentVersion.Length > 32)
            {
                agentVersion = agentVersion[..32];
            }

            var deviceId = Guid.NewGuid();
            var token = GenerateDeviceToken();
            var tokenHash = ComputeSha256Hex(token);

            if (!_devicesByChildGuid.TryGetValue(key, out var devices))
            {
                devices = new List<PairedDevice>();
                _devicesByChildGuid[key] = devices;
            }

            // Defensive: ensure no duplicate DeviceId (should never happen).
            while (devices.Any(d => d.DeviceId == deviceId))
            {
                deviceId = Guid.NewGuid();
            }

            devices.Add(new PairedDevice(
                DeviceId: deviceId,
                DeviceName: deviceName,
                AgentVersion: agentVersion,
                PairedAtUtc: DateTimeOffset.UtcNow,
                TokenHashSha256: tokenHash));

            // One-time code is consumed.
            _pendingPairingByChildGuid.Remove(key);

            EnsureChildProfileUnsafe_NoLock(childId);
            PersistUnsafe_NoLock();

            return new PairingCompleteResponse(childId, deviceId, token, DateTimeOffset.UtcNow);
        }
    }

    public ChildAgentStatus UpsertStatus(ChildId childId, ChildAgentHeartbeatRequest req, EffectiveChildState effective, bool authenticated, Guid? deviceId)
    {
        lock (_gate)
        {
            var key = childId.Value.ToString();

            // K4: Store a privacy-first screen time summary for parent reporting.
            _policiesByChildGuid.TryGetValue(key, out var policy);

            int? limit = policy?.DailyScreenTimeLimitMinutes;
            if (limit is not null && limit.Value > 0)
            {
                var extra = GetActiveExtraScreenTimeMinutesUnsafe_NoLock(childId, DateTimeOffset.UtcNow);
                if (extra > 0)
                {
                    limit = limit.Value + extra;
                }
            }
            int? usedMins = null;
            int? remainingMins = null;
            bool depleted = false;

            if (req.ScreenTime is not null)
            {
                usedMins = Math.Max(0, req.ScreenTime.UsedSecondsToday / 60);
                if (limit is not null && limit.Value > 0)
                {
                    remainingMins = Math.Max(0, limit.Value - usedMins.Value);
                    depleted = usedMins.Value >= limit.Value || req.ScreenTime.BudgetDepleted;
                }
            }

            // K5: apps usage + blocked attempts rollups
            var blockedTotal = 0;
            BlockedAttemptItem[]? topBlocked = null;
            AppUsageItem[]? topUsage = null;
            if (req.Apps is not null)
            {
                blockedTotal = req.Apps.BlockedAttempts?.Sum(b => b.Count) ?? 0;
                topBlocked = req.Apps.BlockedAttempts;
                topUsage = req.Apps.Usage;
            }

            // K6: web rollups
            var webBlockedConfigured = req.Web?.BlockedDomainsConfigured ?? 0;
            var webTopBlocked = req.Web?.TopBlockedDomains;
            var webAlerts = req.Web?.AlertsToday ?? 0;
            var circumvention = req.Circumvention;
            var tamper = req.Tamper;

            // K10+: Gate device integrity signals and alert activity based on policy.
            // Defaults are permissive (true) for backward compatibility.
            var allowCircSignals = (policy?.WebCircumventionDetectionEnabled ?? true);
            var allowTamperSignals = (policy?.DeviceTamperDetectionEnabled ?? true);
            var allowCircAlerts = (policy?.WebCircumventionAlertsEnabled ?? true);
            var allowTamperAlerts = (policy?.DeviceTamperAlertsEnabled ?? true);

            if (!allowCircSignals || !allowCircAlerts) circumvention = null;
            if (!allowTamperSignals || !allowTamperAlerts) tamper = null;

            // Activity events are edge-triggered on signal transitions to reduce noise.
            try
            {
                var prev = _statusByChildGuid.TryGetValue(key, out var prior) ? prior : null;

                bool prevCirc = prev?.Circumvention is not null && (prev.Circumvention.VpnSuspected || prev.Circumvention.ProxyEnabled || prev.Circumvention.PublicDnsDetected || prev.Circumvention.HostsWriteFailed);
                bool nowCirc = circumvention is not null && (circumvention.VpnSuspected || circumvention.ProxyEnabled || circumvention.PublicDnsDetected || circumvention.HostsWriteFailed);

                bool prevTamper = prev?.Tamper is not null && (prev.Tamper.NotRunningElevated || prev.Tamper.EnforcementError);
                bool nowTamper = tamper is not null && (tamper.NotRunningElevated || tamper.EnforcementError);

                var evts = new System.Collections.Generic.List<object>();

                if (allowCircAlerts && nowCirc && !prevCirc)
                {
                    evts.Add(new { kind = "device_circumvention_detected", occurredAtUtc = DateTimeOffset.UtcNow.ToString("O"), vpnSuspected = circumvention!.VpnSuspected, proxyEnabled = circumvention.ProxyEnabled, publicDnsDetected = circumvention.PublicDnsDetected, hostsWriteFailed = circumvention.HostsWriteFailed });
                }
                if (allowTamperAlerts && nowTamper && !prevTamper)
                {
                    evts.Add(new { kind = "device_tamper_detected", occurredAtUtc = DateTimeOffset.UtcNow.ToString("O"), notRunningElevated = tamper!.NotRunningElevated, enforcementError = tamper.EnforcementError, lastError = tamper.LastError, lastErrorAtUtc = tamper.LastErrorAtUtc });
                }

                if (evts.Count > 0)
                {
                    var evtJson = JsonSerializer.Serialize(evts, JsonDefaults.Options);
                    AppendLocalActivityJsonUnsafe_NoLock(childId.Value.ToString(), evtJson);
                }
            }
            catch { }

            var status = new ChildAgentStatus(
                ChildId: childId,
                LastSeenUtc: DateTimeOffset.UtcNow,
                DeviceName: req.DeviceName,
                AgentVersion: req.AgentVersion,
                EffectiveMode: effective.EffectiveMode,
                ReasonCode: effective.ReasonCode,
                PolicyVersion: effective.PolicyVersion,
                Authenticated: authenticated,
                DeviceId: deviceId,
                ScreenTimeLimitMinutes: limit,
                ScreenTimeUsedMinutes: usedMins,
                ScreenTimeRemainingMinutes: remainingMins,
                ScreenTimeBudgetDepleted: depleted,
                ActiveSchedule: effective.ActiveSchedule,
                BlockedAttemptsToday: blockedTotal,
                TopBlockedApps: topBlocked,
                TopAppUsage: topUsage,
                WebBlockedDomainsConfigured: webBlockedConfigured,
                WebTopBlockedDomains: webTopBlocked,
                WebAlertsToday: webAlerts,
                Circumvention: circumvention,
                Tamper: tamper);

            _statusByChildGuid[key] = status;

            EnsureChildProfileUnsafe_NoLock(childId);
            PersistUnsafe_NoLock();
            return status;
        }
    }

    public AccessRequest CreateOrGetRequest(ChildId childId, CreateAccessRequestRequest req)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var requestId = req.RequestId ?? Guid.NewGuid();

            var existingById = _requests.FirstOrDefault(r => r.RequestId == requestId);
            if (existingById is not null)
            {
                return existingById;
            }

            var target = string.IsNullOrWhiteSpace(req.Target)
                ? (req.Type == AccessRequestType.MoreTime ? "screen_time" : "")
                : req.Target!.Trim();

            // Small dedupe window to avoid spam (type+target+pending).
            var windowStart = now.AddMinutes(-2);
            var existingWindow = _requests
                .Where(r => r.ChildId.Value == childId.Value)
                .Where(r => r.Status == AccessRequestStatus.Pending)
                .Where(r => r.Type == req.Type)
                .Where(r => string.Equals(r.Target, target, StringComparison.OrdinalIgnoreCase))
                .Where(r => r.CreatedAtUtc >= windowStart)
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefault();

            if (existingWindow is not null)
            {
                return existingWindow;
            }

            var created = new AccessRequest(
                RequestId: requestId,
                ChildId: childId,
                Type: req.Type,
                Target: target,
                Reason: string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason!.Trim(),
                CreatedAtUtc: now);

            _requests.Add(created);
            EnsureChildProfileUnsafe_NoLock(childId);

            try
            {
                var evt = JsonSerializer.Serialize(new[] { new { kind = "request_created", requestId = created.RequestId, type = created.Type.ToString(), target = created.Target, reason = created.Reason, occurredAtUtc = now.ToString("O") } }, JsonDefaults.Options);
                AppendLocalActivityJsonUnsafe_NoLock(childId.Value.ToString(), evt);
            }
            catch { }

            PersistUnsafe_NoLock();
            return created;
        }
    }

    public IReadOnlyList<AccessRequest> GetRequests(ChildId? childId = null, AccessRequestStatus? status = null, int take = 200)
    {
        lock (_gate)
        {
            IEnumerable<AccessRequest> q = _requests;
            if (childId is not null)
            {
                q = q.Where(r => r.ChildId == childId.Value);
            }
            if (status is not null)
            {
                q = q.Where(r => r.Status == status);
            }
            return q
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(Math.Clamp(take, 1, 1000))
                .ToList();
        }
    }

    public bool TryDecideRequest(Guid requestId, DecideAccessRequestRequest decision, out AccessRequest updated, out Grant? createdGrant)
    {
        lock (_gate)
        {
            createdGrant = null;
            var idx = _requests.FindIndex(r => r.RequestId == requestId);
            if (idx < 0)
            {
                updated = null!;
                return false;
            }

            var req = _requests[idx];
            if (req.Status != AccessRequestStatus.Pending)
            {
                // idempotent: return already-decided request
                updated = req;
                return true;
            }

            var now = DateTimeOffset.UtcNow;
            var decidedBy = string.IsNullOrWhiteSpace(decision.DecidedBy) ? "parent" : decision.DecidedBy!.Trim();

            var accessDecision = new AccessDecision(
                Approved: decision.Approve,
                ExtraMinutes: decision.ExtraMinutes,
                DurationMinutes: decision.DurationMinutes,
                Notes: string.IsNullOrWhiteSpace(decision.Notes) ? null : decision.Notes!.Trim());

            updated = req with
            {
                Status = decision.Approve ? AccessRequestStatus.Approved : AccessRequestStatus.Denied,
                DecidedAtUtc = now,
                DecidedBy = decidedBy,
                Decision = accessDecision
            };

            _requests[idx] = updated;

            if (decision.Approve)
            {
                createdGrant = CreateGrantUnsafe_NoLock(updated, decision, now);
                if (createdGrant is not null)
                {
                    _grants.Add(createdGrant);
                }
            }

            
            try
            {
                var events = new System.Collections.Generic.List<object>();
                events.Add(new { kind = "request_decided", requestId = updated.RequestId, approved = decision.Approve, type = updated.Type.ToString(), target = updated.Target, occurredAtUtc = now.ToString("O"), decidedBy = decidedBy, extraMinutes = decision.ExtraMinutes, durationMinutes = decision.DurationMinutes });
                if (createdGrant is not null)
                {
                    events.Add(new { kind = "grant_created", grantId = createdGrant.GrantId, type = createdGrant.Type.ToString(), target = createdGrant.Target, expiresAtUtc = createdGrant.ExpiresAtUtc.ToString("O"), occurredAtUtc = now.ToString("O"), sourceRequestId = updated.RequestId });
                }
                var evtJson = JsonSerializer.Serialize(events, JsonDefaults.Options);
                AppendLocalActivityJsonUnsafe_NoLock(updated.ChildId.Value.ToString(), evtJson);
            }
            catch { }

            PersistUnsafe_NoLock();
            return true;
        }
    }


    public bool TryGetLatestDiagnosticsBundle(ChildId childId, out DiagnosticsBundleInfo info)
    {
        lock (_gate)
        {
            return _diagnosticsByChildGuid.TryGetValue(childId.Value.ToString(), out info!);
        }
    }

    public DiagnosticsBundleInfo UpsertDiagnosticsBundle(ChildId childId, string fileName, long sizeBytes, DateTimeOffset createdAtUtc)
    {
        lock (_gate)
        {
            var info = new DiagnosticsBundleInfo(childId, createdAtUtc, sizeBytes, fileName);
            _diagnosticsByChildGuid[childId.Value.ToString()] = info;
            PersistUnsafe_NoLock();
            return info;
        }
    }

    public IReadOnlyList<Grant> GetActiveGrants(ChildId childId, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            return _grants
                .Where(g => g.ChildId.Value == childId.Value)
                .Where(g => g.ExpiresAtUtc > nowUtc)
                .OrderByDescending(g => g.CreatedAtUtc)
                .ToList();
        }
    }

    // K9: Diagnostics bundle storage.
    public DiagnosticsBundleInfo SaveDiagnosticsBundle(ChildId childId, string fileName, Stream zipStream)
    {
        lock (_gate)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Safe0ne",
                "DashboardServer",
                "diagnostics",
                childId.Value.ToString());
            Directory.CreateDirectory(dir);

            var safeName = string.IsNullOrWhiteSpace(fileName) ? "bundle.zip" : Path.GetFileName(fileName);
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(dir, $"bundle_{stamp}_{safeName}");

            using (var fs = File.Create(path))
            {
                zipStream.CopyTo(fs);
            }

            var fi = new FileInfo(path);
            var info = new DiagnosticsBundleInfo(
                ChildId: childId,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                SizeBytes: fi.Length,
                FileName: fi.Name);

            _diagnosticsByChildGuid[childId.Value.ToString()] = info;
            PersistUnsafe_NoLock();
            return info;
        }
    }

    public bool TryGetLatestDiagnosticsBundle(ChildId childId, out DiagnosticsBundleInfo info, out string fullPath)
    {
        lock (_gate)
        {
            if (!_diagnosticsByChildGuid.TryGetValue(childId.Value.ToString(), out info!))
            {
                fullPath = string.Empty;
                return false;
            }

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Safe0ne",
                "DashboardServer",
                "diagnostics",
                childId.Value.ToString());
            fullPath = Path.Combine(dir, info.FileName);
            if (!File.Exists(fullPath))
            {
                return false;
            }
            return true;
        }
    }


    private int GetActiveExtraScreenTimeMinutesUnsafe_NoLock(ChildId childId, DateTimeOffset nowUtc)
    {
        return _grants
            .Where(g => g.ChildId.Value == childId.Value)
            .Where(g => g.Type == GrantType.ExtraScreenTime)
            .Where(g => g.ExpiresAtUtc > nowUtc)
            .Select(g => g.ExtraMinutes ?? 0)
            .Sum();
    }

    private static Grant? CreateGrantUnsafe_NoLock(AccessRequest req, DecideAccessRequestRequest decision, DateTimeOffset nowUtc)
    {
        switch (req.Type)
        {
            case AccessRequestType.MoreTime:
            {
                var extra = decision.ExtraMinutes ?? 15;
                extra = Math.Clamp(extra, 1, 24 * 60);

                // Expire at local midnight (Windows-first prototype: parent/child share local timezone).
                var localNow = DateTimeOffset.Now;
                var localMidnightNext = localNow.Date.AddDays(1);
                var expiresLocal = new DateTimeOffset(localMidnightNext, localNow.Offset);
                var expiresUtc = expiresLocal.ToUniversalTime();

                return new Grant(
                    GrantId: Guid.NewGuid(),
                    ChildId: req.ChildId,
                    Type: GrantType.ExtraScreenTime,
                    Target: "screen_time",
                    CreatedAtUtc: nowUtc,
                    ExpiresAtUtc: expiresUtc,
                    ExtraMinutes: extra,
                    SourceRequestId: req.RequestId);
            }
            case AccessRequestType.UnblockApp:
            {
                var dur = decision.DurationMinutes ?? 30;
                dur = Math.Clamp(dur, 1, 24 * 60);
                return new Grant(
                    GrantId: Guid.NewGuid(),
                    ChildId: req.ChildId,
                    Type: GrantType.UnblockApp,
                    Target: req.Target,
                    CreatedAtUtc: nowUtc,
                    ExpiresAtUtc: nowUtc.AddMinutes(dur),
                    ExtraMinutes: null,
                    SourceRequestId: req.RequestId);
            }
            case AccessRequestType.UnblockSite:
            {
                var dur = decision.DurationMinutes ?? 30;
                dur = Math.Clamp(dur, 1, 24 * 60);
                return new Grant(
                    GrantId: Guid.NewGuid(),
                    ChildId: req.ChildId,
                    Type: GrantType.UnblockSite,
                    Target: req.Target,
                    CreatedAtUtc: nowUtc,
                    ExpiresAtUtc: nowUtc.AddMinutes(dur),
                    ExtraMinutes: null,
                    SourceRequestId: req.RequestId);
            }
            default:
                return null;
        }
    }


    public ChildCommand CreateCommand(ChildId childId, CreateChildCommandRequest req)
{
    lock (_gate)
    {
        var key = childId.Value.ToString();
        if (!_commandsByChildGuid.TryGetValue(key, out var list))
        {
            list = new List<ChildCommand>();
            _commandsByChildGuid[key] = list;
        }

        DateTimeOffset? expires = null;
        if (req.ExpiresInMinutes is not null && req.ExpiresInMinutes.Value > 0)
        {
            expires = DateTimeOffset.UtcNow.AddMinutes(req.ExpiresInMinutes.Value);
        }

        var cmd = new ChildCommand(
            CommandId: Guid.NewGuid(),
            Type: req.Type,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            PayloadJson: req.PayloadJson,
            ExpiresAtUtc: expires);

        list.Add(cmd);

        EnsureChildProfileUnsafe_NoLock(childId);
        PersistUnsafe_NoLock();
        return cmd;
    }
}

public IReadOnlyList<ChildCommand> GetCommands(ChildId childId, int take = 50)
{
    lock (_gate)
    {
        var key = childId.Value.ToString();
        if (!_commandsByChildGuid.TryGetValue(key, out var list) || list.Count == 0)
        {
            return Array.Empty<ChildCommand>();
        }

        // newest first
        return list
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(Math.Max(1, take))
            .ToList();
    }
}

public IReadOnlyList<ChildCommand> GetPendingCommands(ChildId childId, int max = 20)
{
    lock (_gate)
    {
        var key = childId.Value.ToString();
        if (!_commandsByChildGuid.TryGetValue(key, out var list) || list.Count == 0)
        {
            return Array.Empty<ChildCommand>();
        }

        var now = DateTimeOffset.UtcNow;
        return list
            .Where(c => !c.Acked && (c.ExpiresAtUtc is null || c.ExpiresAtUtc > now))
            .OrderBy(c => c.CreatedAtUtc)
            .Take(Math.Max(1, max))
            .ToList();
    }
}

public bool TryAckCommand(ChildId childId, Guid commandId, Guid deviceId, AckChildCommandRequest req, out ChildCommand updated)
{
    lock (_gate)
    {
        updated = default!;
        var key = childId.Value.ToString();
        if (!_commandsByChildGuid.TryGetValue(key, out var list) || list.Count == 0)
        {
            return false;
        }

        var idx = list.FindIndex(c => c.CommandId == commandId);
        if (idx < 0) return false;

        var existing = list[idx];
        if (existing.Acked)
        {
            updated = existing;
            return true;
        }

        updated = existing with
        {
            Acked = true,
            AckedAtUtc = DateTimeOffset.UtcNow,
            AckedByDeviceId = deviceId,
            AckResult = req.Result,
            AckDetail = req.Detail
        };

        list[idx] = updated;

        PersistUnsafe_NoLock();
        return true;
    }
}

public ChildPolicy UpsertPolicy(ChildId childId, UpdateChildPolicyRequest req)
    {
        lock (_gate)
        {
            var key = childId.Value.ToString();
            if (!_policiesByChildGuid.TryGetValue(key, out var existing))
            {
                existing = new ChildPolicy(
                    ChildId: childId,
                    Version: PolicyVersion.Initial,
                    Mode: SafetyMode.Open,
                    UpdatedAtUtc: DateTimeOffset.UtcNow,
                    UpdatedBy: "system",
                    BlockedProcessNames: new[] { "notepad.exe" });
            }

            // Patch semantics: optional fields override when provided.
            var alwaysAllowed = req.AlwaysAllowed ?? existing.AlwaysAllowed;

            // Optional: block list is stored on the policy but only enforced for certain modes (agent-side).
            var blocked = req.BlockedProcessNames ?? existing.BlockedProcessNames;

            // K4/P6: screen time + schedules
            var dailyLimit = req.DailyScreenTimeLimitMinutes ?? existing.DailyScreenTimeLimitMinutes;
            var bedtime = req.BedtimeWindow ?? existing.BedtimeWindow;
            var school = req.SchoolWindow ?? existing.SchoolWindow;
            var homework = req.HomeworkWindow ?? existing.HomeworkWindow;

            // K5/P7: apps & games
            var appsAllowListEnabled = req.AppsAllowListEnabled ?? existing.AppsAllowListEnabled;
            var allowedApps = req.AllowedProcessNames ?? existing.AllowedProcessNames;
            var perAppLimits = req.PerAppDailyLimits ?? existing.PerAppDailyLimits;

            // K6/P8: web & content filtering
            var webAdult = req.WebAdultBlockEnabled ?? existing.WebAdultBlockEnabled;
            var webRules = req.WebCategoryRules ?? existing.WebCategoryRules;
            var webAllow = req.WebAllowedDomains ?? existing.WebAllowedDomains;
            var webBlock = req.WebBlockedDomains ?? existing.WebBlockedDomains;
            var webCirc = req.WebCircumventionDetectionEnabled ?? existing.WebCircumventionDetectionEnabled;
            var webSafe = req.WebSafeSearchEnabled ?? existing.WebSafeSearchEnabled;


            // K10+: device integrity signals gates
            var tamperDetect = req.DeviceTamperDetectionEnabled ?? existing.DeviceTamperDetectionEnabled;
            var tamperAlerts = req.DeviceTamperAlertsEnabled ?? existing.DeviceTamperAlertsEnabled;
            var circAlerts = req.WebCircumventionAlertsEnabled ?? existing.WebCircumventionAlertsEnabled;


            DateTimeOffset? grantUntil = existing.GrantUntilUtc;
            if (req.GrantMinutes is not null)
            {
                // GrantMinutes <= 0 clears the grant.
                grantUntil = req.GrantMinutes.Value <= 0
                    ? null
                    : DateTimeOffset.UtcNow.AddMinutes(req.GrantMinutes.Value);
            }

            var updated = existing with
            {
                Version = existing.Version.Next(),
                Mode = req.Mode,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedBy = req.UpdatedBy,
                AlwaysAllowed = alwaysAllowed,
                GrantUntilUtc = grantUntil,
                BlockedProcessNames = blocked,
                DailyScreenTimeLimitMinutes = dailyLimit,
                BedtimeWindow = bedtime,
                SchoolWindow = school,
                HomeworkWindow = homework,

                // K5/K6: apps + web config (persist the UI payload; enforcement is best-effort agent-side)
                AppsAllowListEnabled = appsAllowListEnabled,
                AllowedProcessNames = allowedApps,
                PerAppDailyLimits = perAppLimits,

                WebAdultBlockEnabled = webAdult,
                WebCategoryRules = webRules,
                WebAllowedDomains = webAllow,
                WebBlockedDomains = webBlock,
                WebCircumventionDetectionEnabled = webCirc,
                WebSafeSearchEnabled = webSafe,
                DeviceTamperDetectionEnabled = tamperDetect,
                DeviceTamperAlertsEnabled = tamperAlerts,
                WebCircumventionAlertsEnabled = circAlerts
            };

            _policiesByChildGuid[key] = updated;

            EnsureChildProfileUnsafe_NoLock(childId);
            PersistUnsafe_NoLock();
            return updated;
        }
    }

    private void LoadOrSeed()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                SeedUnsafe_NoLock();
                PersistUnsafe_NoLock();
                return;
            }

            try
            {
                var json = File.ReadAllText(_path);
                var state = JsonSerializer.Deserialize<ControlPlaneState>(json, JsonDefaults.Options);

                if (state?.Children is null || state.Policies is null)
                {
                    SeedUnsafe_NoLock();
                    PersistUnsafe_NoLock();
                    return;
                }

                _children = state.Children;
                _archivedAtByChildGuid = (state.ArchivedChildren ?? new List<ArchivedChildState>())
                    .ToDictionary(a => a.ChildId.Value.ToString(), a => a.ArchivedAtUtc, StringComparer.OrdinalIgnoreCase);

                _localChildMetaJsonByChildGuid = (state.LocalChildMeta ?? new List<LocalChildMetaState>())
                    .ToDictionary(a => a.ChildId.Value.ToString(), a => a.MetaJson, StringComparer.OrdinalIgnoreCase);

                _localSettingsProfileJsonByChildGuid = (state.LocalSettingsProfiles ?? new List<LocalSettingsProfileState>())
                    .ToDictionary(a => a.ChildId.Value.ToString(), a => a.ProfileJson, StringComparer.OrdinalIgnoreCase);

                _localActivityEventsJsonByChildGuid = (state.LocalActivityEvents ?? new List<LocalActivityEventsState>())
                    .ToDictionary(a => a.ChildId.Value.ToString(), a => a.EventsJson, StringComparer.OrdinalIgnoreCase);

                _localLastLocationJsonByChildGuid = (state.LocalLocations ?? new List<LocalLocationState>())
                    .ToDictionary(a => a.ChildId.Value.ToString(), a => a.LocationJson, StringComparer.OrdinalIgnoreCase);


                _policiesByChildGuid = state.Policies
                    .ToDictionary(p => p.ChildId.Value.ToString(), p => p, StringComparer.OrdinalIgnoreCase);

                _statusByChildGuid = (state.Statuses ?? new List<ChildAgentStatus>())
                    .ToDictionary(s => s.ChildId.Value.ToString(), s => s, StringComparer.OrdinalIgnoreCase);

                _devicesByChildGuid = (state.Devices ?? new List<ChildDevicesState>())
                    .ToDictionary(
                        d => d.ChildId.Value.ToString(),
                        d => d.Devices,
                        StringComparer.OrdinalIgnoreCase);

                _pendingPairingByChildGuid = (state.PendingPairings ?? new List<PendingPairing>())
                    .Where(p => p.ExpiresAtUtc > DateTimeOffset.UtcNow)
                    .ToDictionary(p => p.ChildId.Value.ToString(), p => p, StringComparer.OrdinalIgnoreCase);

                _commandsByChildGuid = (state.Commands ?? new List<ChildCommandsState>())
                    .ToDictionary(
                        c => c.ChildId.Value.ToString(),
                        c => c.Commands,
                        StringComparer.OrdinalIgnoreCase);

                _requests = state.Requests ?? new List<AccessRequest>();

                // Drop expired grants at load; they should never be active after restart.
                var now = DateTimeOffset.UtcNow;
                _grants = (state.Grants ?? new List<Grant>())
                    .Where(g => g.ExpiresAtUtc > now.AddDays(-1))
                    .ToList();

                _diagnosticsByChildGuid = (state.DiagnosticsBundles ?? new List<DiagnosticsBundleInfo>())
                    .ToDictionary(d => d.ChildId.Value.ToString(), d => d, StringComparer.OrdinalIgnoreCase);


                var loadedSchema = 0;
                try { loadedSchema = state.SchemaVersion; } catch { loadedSchema = 0; }
                if (loadedSchema <= 0) loadedSchema = 1;

                // Lazy upgrade: keep reading older snapshots but re-persist in the latest schema.
                if (loadedSchema < CurrentSchemaVersion)
                {
                    PersistUnsafe_NoLock();
                }
            }
            catch
            {
                // Corrupt/partial file: fall back to seed.
                SeedUnsafe_NoLock();
                PersistUnsafe_NoLock();
            }
        }
    }

    private void SeedUnsafe_NoLock()
    {
        // Deterministic seed so UI and integration tests can target a known child.
        var childId = new ChildId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        _children = new List<ChildProfile>
        {
            new(childId, "Demo Child")
        };

        _policiesByChildGuid = new Dictionary<string, ChildPolicy>(StringComparer.OrdinalIgnoreCase)
        {
            [childId.Value.ToString()] = new ChildPolicy(
                ChildId: childId,
                Version: PolicyVersion.Initial,
                Mode: SafetyMode.Open,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedBy: "system",
                GrantUntilUtc: null,
                AlwaysAllowed: false)
        };

        _statusByChildGuid = new Dictionary<string, ChildAgentStatus>(StringComparer.OrdinalIgnoreCase);
        _devicesByChildGuid = new Dictionary<string, List<PairedDevice>>(StringComparer.OrdinalIgnoreCase);
        _pendingPairingByChildGuid = new Dictionary<string, PendingPairing>(StringComparer.OrdinalIgnoreCase);
        _commandsByChildGuid = new Dictionary<string, List<ChildCommand>>(StringComparer.OrdinalIgnoreCase);
        _diagnosticsByChildGuid = new Dictionary<string, DiagnosticsBundleInfo>(StringComparer.OrdinalIgnoreCase);

        _localLastLocationJsonByChildGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        _requests = new List<AccessRequest>();
        _grants = new List<Grant>();
    }

    private void PersistUnsafe_NoLock()
    {
        var devices = _devicesByChildGuid
            .Select(kvp => new ChildDevicesState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value))
            .ToList();

        var commands = _commandsByChildGuid
            .Select(kvp => new ChildCommandsState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value))
            .ToList();

        var state = new ControlPlaneState(
            Children: _children,
            Policies: _policiesByChildGuid.Values.ToList(),
            Statuses: _statusByChildGuid.Values.ToList(),
            Devices: devices,
            PendingPairings: _pendingPairingByChildGuid.Values.ToList(),
            Commands: commands,
            Requests: _requests,
            Grants: _grants,
            DiagnosticsBundles: _diagnosticsByChildGuid.Values.ToList(),
            ArchivedChildren: _archivedAtByChildGuid.Select(kvp => new ArchivedChildState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value)).ToList(),
            LocalChildMeta: _localChildMetaJsonByChildGuid.Select(kvp => new LocalChildMetaState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value)).ToList(),
            LocalSettingsProfiles: _localSettingsProfileJsonByChildGuid.Select(kvp => new LocalSettingsProfileState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value)).ToList(),
            LocalActivityEvents: _localActivityEventsJsonByChildGuid.Select(kvp => new LocalActivityEventsState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value)).ToList(),
            LocalLocations: _localLastLocationJsonByChildGuid.Select(kvp => new LocalLocationState(new ChildId(Guid.Parse(kvp.Key)), kvp.Value)).ToList(),
            SchemaVersion: CurrentSchemaVersion
        );

        var json = JsonSerializer.Serialize(state, JsonDefaults.Options);

        // Safer replace: write temp then atomically swap into place on Windows.
        // If the destination doesn't exist yet, fall back to move.
        // See File.Replace docs.
        var tmp = _path + ".tmp";
        var bak = _path + ".bak";
        File.WriteAllText(tmp, json);
        if (File.Exists(_path))
        {
            File.Replace(tmp, _path, bak, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, _path);
        }
        if (File.Exists(tmp)) File.Delete(tmp);
        // Best-effort: don't accumulate backups forever.
        if (File.Exists(bak))
        {
            try { File.Delete(bak); } catch { /* ignore */ }
        }
    }

    private void EnsureChildProfileUnsafe_NoLock(ChildId childId)
    {
        if (_children.All(c => c.Id.Value != childId.Value))
        {
            _children.Add(new ChildProfile(childId, "Unnamed Child"));
        }
    }

    private static string GenerateNumericCode(int digits)
    {
        // Cryptographically strong code.
        var bytes = RandomNumberGenerator.GetBytes(8);
        ulong val = BitConverter.ToUInt64(bytes, 0);
        var mod = (ulong)Math.Pow(10, digits);
        return (val % mod).ToString().PadLeft(digits, '0');
    }



    private sealed record PairedDevice(
        Guid DeviceId,
        string DeviceName,
        string AgentVersion,
        DateTimeOffset PairedAtUtc,
        string TokenHashSha256);

    private sealed record PendingPairing(
        ChildId ChildId,
        string PairingCode,
        DateTimeOffset ExpiresAtUtc);

    private sealed record ChildDevicesState(
        ChildId ChildId,
        List<PairedDevice> Devices);

    // Local mode: child archive state (soft-delete), metadata, and UI settings profile blobs.
    // These are persisted as part of ControlPlaneState to survive restarts.
    private sealed record ArchivedChildState(
        ChildId ChildId,
        DateTimeOffset ArchivedAtUtc);

    private sealed record LocalChildMetaState(
        ChildId ChildId,
        string MetaJson);

    private sealed record LocalSettingsProfileState(
        ChildId ChildId,
        string ProfileJson);

    private sealed record LocalActivityEventsState(
        ChildId ChildId,
        string EventsJson);

    private sealed record ChildCommandsState(
        ChildId ChildId,
        List<ChildCommand> Commands);

    private sealed record ControlPlaneState(
        List<ChildProfile> Children,
        List<ChildPolicy> Policies,
        List<ChildAgentStatus>? Statuses = null,
        List<ChildDevicesState>? Devices = null,
        List<PendingPairing>? PendingPairings = null,
        List<ChildCommandsState>? Commands = null,
        // K8/P11: requests and time-boxed grants.
        List<AccessRequest>? Requests = null,
        List<Grant>? Grants = null,
        // K9: latest diagnostics bundle per child
        List<DiagnosticsBundleInfo>? DiagnosticsBundles = null,
        // Local mode: archive/restore without deleting
        List<ArchivedChildState>? ArchivedChildren = null,
        List<LocalChildMetaState>? LocalChildMeta = null,
        List<LocalSettingsProfileState>? LocalSettingsProfiles = null,
        List<LocalActivityEventsState>? LocalActivityEvents = null,
        List<LocalLocationState>? LocalLocations = null,
        int SchemaVersion = CurrentSchemaVersion);

    private sealed record LocalLocationState(
        ChildId ChildId,
        string LocationJson);
}
