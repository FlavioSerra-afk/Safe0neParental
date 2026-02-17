using System.Text.Json;
using System.Text.Json.Nodes;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

public sealed partial class JsonFileControlPlane
{
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
