// LEGACY-COMPAT: Device token revoke canonicalization.
// RemoveAfter: Wave1-Stability (Canonical Tokens API) | Tracking: Docs/00_Shared/Legacy-Code-Registry.md
//
// Canonical behavior: revoking a device token MUST NOT delete the paired device record.
// It should make authentication fail for the existing token while preserving device metadata.

#nullable enable
using System;
using System.Linq;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

public sealed partial class JsonFileControlPlane
{
    /// <summary>
    /// Canonical internal revoke implementation.
    /// - Keeps the paired device record.
    /// - Invalidates the current token by replacing its stored hash.
    /// - Returns the owning child id when found.
    /// </summary>
    private bool TryRevokeDeviceToken_Internal(
        Guid deviceId,
        string revokedBy,
        string? reason,
        out ChildId? owningChildId,
        out bool revoked,
        out string? error)
    {
        lock (_gate)
        {
            owningChildId = null;
            revoked = false;
            error = null;

            try
            {
                if (_devicesByChildGuid.Count == 0)
                {
                    return true;
                }

                foreach (var kvp in _devicesByChildGuid.ToList())
                {
                    var devices = kvp.Value;
                    if (devices is null || devices.Count == 0) continue;

                    var idx = devices.FindIndex(d => d.DeviceId == deviceId);
                    if (idx < 0) continue;

                    // Invalidate token without deleting the device.
                    // NOTE: we intentionally do not persist extra revoke metadata here to avoid schema churn
                    // in the current snapshot format.
                    var replacementToken = GenerateDeviceToken();
                    var replacementHash = ComputeSha256Hex(replacementToken);

                    var prior = devices[idx];
                    devices[idx] = new PairedDevice(
                        DeviceId: prior.DeviceId,
                        DeviceName: prior.DeviceName,
                        AgentVersion: prior.AgentVersion,
                        PairedAtUtc: prior.PairedAtUtc,
                        TokenHashSha256: replacementHash);

                    revoked = true;

                    if (Guid.TryParse(kvp.Key, out var childGuid))
                    {
                        owningChildId = new ChildId(childGuid);
                    }

                    PersistUnsafe_NoLock();
                    return true;
                }

                // Not found is not an error.
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                owningChildId = null;
                revoked = false;
                return false;
            }
        }
    }
}
