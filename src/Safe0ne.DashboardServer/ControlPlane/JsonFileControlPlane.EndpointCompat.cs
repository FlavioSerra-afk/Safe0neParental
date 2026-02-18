using System.Text.Json.Nodes;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

// Endpoint-facing compatibility helpers.
// These methods exist so Program.cs can call stable names even as we split JsonFileControlPlane into domain partials.
public sealed partial class JsonFileControlPlane
{
    /// <summary>
    /// Revoke a paired device token by removing the paired device entry.
    /// Returns true if the operation executed; 'revoked' indicates whether a device was actually removed.
    /// </summary>
    public bool TryRevokeDeviceToken(Guid deviceId, out bool revoked, out string? error)
    {
        lock (_gate)
        {
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
                    if (idx >= 0)
                    {
                        devices.RemoveAt(idx);
                        revoked = true;

                        // Clean empty lists to keep snapshots tidy.
                        if (devices.Count == 0)
                        {
                            _devicesByChildGuid.Remove(kvp.Key);
                        }

                        PersistUnsafe_NoLock();
                        return true;
                    }
                }

                // Not found is not an error; caller decides how to surface.
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// Overload that accepts childId for API routing convenience; deviceId is still the primary key.
    /// </summary>
    public bool TryRevokeDeviceToken(Guid childId, Guid deviceId, out bool revoked, out string? error)
    {
        // childId is advisory; we still search defensively to avoid SSOT drift.
        return TryRevokeDeviceToken(deviceId, out revoked, out error);
    }

    /// <summary>
    /// Attempts to roll back the local settings profile JSON to a last-known-good snapshot if the current profile embeds one.
    /// This is deliberately conservative and additive: if no embedded LKG exists, it reports 'rolledBack=false'.
    /// </summary>
    public bool TryRollbackPolicyToLastKnownGood(Guid childId, out bool rolledBack, out string? error)
        => TryRollbackPolicyToLastKnownGood_Internal(new ChildId(childId), "system", out rolledBack, out error);

    /// <summary>
    /// Convenience overload for ChildId value object.
    /// </summary>
    public bool TryRollbackPolicyToLastKnownGood(ChildId childId, out bool rolledBack, out string? error)
        => TryRollbackPolicyToLastKnownGood_Internal(childId, "system", out rolledBack, out error);
    /// <summary>
    /// Endpoint-facing signature used by Program.cs during modular refactors.
    /// 'requestedBy' is currently informational (future: activity/audit).
    /// </summary>
    public bool TryRollbackPolicyToLastKnownGood(ChildId childId, string? requestedBy, out bool rolledBack, out string? error)
        => TryRollbackPolicyToLastKnownGood_Internal(childId, string.IsNullOrWhiteSpace(requestedBy) ? "system" : requestedBy!, out rolledBack, out error);

    /// <summary>
    /// Endpoint-facing signature used by Program.cs during modular refactors.
    /// </summary>
    public bool TryRollbackPolicyToLastKnownGood(Guid childId, string? requestedBy, out bool rolledBack, out string? error)
        => TryRollbackPolicyToLastKnownGood_Internal(new ChildId(childId), string.IsNullOrWhiteSpace(requestedBy) ? "system" : requestedBy!, out rolledBack, out error);

    /// <summary>
    /// Endpoint-facing token revoke signature used by Program.cs.
    /// Removes the device entry and returns the owning childId if found.
    /// </summary>
    public bool TryRevokeDeviceToken(Guid deviceId, string revokedBy, string? reason, out ChildId? childId)
    {
        lock (_gate)
        {
            childId = null;

            try
            {
                if (_devicesByChildGuid.Count == 0)
                    return false;

                foreach (var kvp in _devicesByChildGuid.ToList())
                {
                    var devices = kvp.Value;
                    if (devices is null || devices.Count == 0) continue;

                    var idx = devices.FindIndex(d => d.DeviceId == deviceId);
                    if (idx < 0) continue;

                    devices.RemoveAt(idx);

                    if (devices.Count == 0)
                        _devicesByChildGuid.Remove(kvp.Key);

                    PersistUnsafe_NoLock();

                    // Keys are persisted as string representation of the child's Guid.
                    // Be defensive in case older data contains non-Guid keys.
                    if (!Guid.TryParse(kvp.Key, out var childGuid))
                        continue;

                    childId = new ChildId(childGuid);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                // Treat errors as not found to keep endpoint stable; caller will surface generic failure.
                childId = null;
                return false;
            }
        }
    }
}
