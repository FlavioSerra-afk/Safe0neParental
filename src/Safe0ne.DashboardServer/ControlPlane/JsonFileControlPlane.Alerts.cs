using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

public sealed partial class JsonFileControlPlane
{
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

}
