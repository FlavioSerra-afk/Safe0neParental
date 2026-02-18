using System;
using System.Text.Json.Nodes;

using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

// Policy rollback is an additive, best-effort safety mechanism.
//
// IMPORTANT:
// - SSOT remains JsonFileControlPlane.
// - We do not introduce a second policy store.
// - Rollback only works when the LocalSettingsProfile contains a "lastKnownGood" snapshot.
//   If no snapshot exists, we return a clear error and do not mutate SSOT.
//
// NOTE ABOUT "LEGACY":
// Some call-sites and tests historically used slightly different method signatures/parameter names.
// We keep these overloads as *compat shims* so we can migrate toward the canonical API surface
// without regressions. These shims are safe to delete once *all* call-sites are migrated.

public partial class JsonFileControlPlane
{
    /// <summary>
    /// Internal rollback implementation (API surface lives in EndpointCompat partial): rolls the child's LocalSettingsProfile "policy" surface back to the
    /// embedded last-known-good snapshot (if present).
    /// </summary>
    private bool TryRollbackPolicyToLastKnownGood_Internal(ChildId childId, string requestedBy, out bool rolledBack, out string? error)
    {
        rolledBack = false;
        error = null;

        var key = childId.Value.ToString();
        lock (_gate)
        {
            if (!_localSettingsProfileJsonByChildGuid.TryGetValue(key, out var current) || string.IsNullOrWhiteSpace(current))
            {
                error = "not_found";
                return false;
            }

            JsonObject? root;
            try
            {
                root = JsonNode.Parse(current) as JsonObject;
            }
            catch
            {
                error = "invalid_profile_json";
                return false;
            }

            if (root is null)
            {
                error = "invalid_profile_json";
                return false;
            }

            // We support two additive snapshot shapes:
            // - lastKnownGood.policy (object)
            // - lastKnownGoodProfile (full profile object)
            //
            // This keeps us tolerant of earlier patch iterations.
            JsonObject? lkgProfile = null;
            JsonObject? lkg = root["lastKnownGood"] as JsonObject;
            if (lkg is not null)
            {
                if (lkg.TryGetPropertyValue("profile", out var p) && p is JsonObject pObj)
                    lkgProfile = pObj;
            }

            if (lkgProfile is null && root.TryGetPropertyValue("lastKnownGoodProfile", out var lkgp) && lkgp is JsonObject lkgpObj)
                lkgProfile = lkgpObj;

            if (lkgProfile is not null)
            {
                lkgProfile["rolledBackAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
                lkgProfile["rolledBackBy"] = string.IsNullOrWhiteSpace(requestedBy) ? "system" : requestedBy.Trim();

                var json = lkgProfile.ToJsonString(JsonDefaults.Options);
                _localSettingsProfileJsonByChildGuid[key] = NormalizeLocalSettingsProfileJson(key, json);
                PersistUnsafe_NoLock();
                rolledBack = true;
                return true;
            }

            JsonObject? lkgPolicy = lkg?["policy"] as JsonObject;
            if (lkgPolicy is null)
            {
                error = "no_last_known_good";
                return false;
            }

            root["policy"] = lkgPolicy.DeepClone();
            root["rolledBackAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
            root["rolledBackBy"] = string.IsNullOrWhiteSpace(requestedBy) ? "system" : requestedBy.Trim();

            _localSettingsProfileJsonByChildGuid[key] = NormalizeLocalSettingsProfileJson(key, root.ToJsonString(JsonDefaults.Options));
            PersistUnsafe_NoLock();
            rolledBack = true;
            return true;
        }
    }
}
