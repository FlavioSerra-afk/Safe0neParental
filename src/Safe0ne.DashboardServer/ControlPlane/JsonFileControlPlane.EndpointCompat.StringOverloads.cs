using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

// String-guid overloads for endpoint compat.
// These exist to keep Program.cs and older route binders compiling while we migrate to canonical Guid/ChildId surfaces.
public sealed partial class JsonFileControlPlane
{
    public bool TryRevokeDeviceToken(string deviceId, out bool revoked, out string? error)
    {
        revoked = false;
        error = null;
        if (!Guid.TryParse(deviceId, out var gid))
        {
            error = "invalid_device_id";
            return true; // not a storage failure; caller may map to 400/404
        }
        return TryRevokeDeviceToken(gid, out revoked, out error);
    }

    public bool TryRevokeDeviceToken(string childId, string deviceId, out bool revoked, out string? error)
    {
        revoked = false;
        error = null;
        if (!Guid.TryParse(deviceId, out var dg))
        {
            error = "invalid_device_id";
            return true;
        }
        // childId is advisory; we still search defensively
        _ = Guid.TryParse(childId, out var _);
        return TryRevokeDeviceToken(dg, out revoked, out error);
    }

    public bool TryRevokeDeviceToken(string deviceId, string revokedBy, string? reason, out ChildId? childId)
    {
        childId = null;
        if (!Guid.TryParse(deviceId, out var gid))
        {
            return false;
        }
        return TryRevokeDeviceToken(gid, revokedBy, reason, out childId);
    }

    public bool TryRevokeDeviceToken(string childId, string deviceId, string revokedBy, string? reason, out ChildId? outChildId)
    {
        outChildId = null;
        if (!Guid.TryParse(deviceId, out var dg))
        {
            return false;
        }
        // childId is advisory, but if parseable we can prefer it later; for now delegate.
        _ = Guid.TryParse(childId, out var _);
        return TryRevokeDeviceToken(dg, revokedBy, reason, out outChildId);
    }

    public bool TryRollbackPolicyToLastKnownGood(string childId, string? requestedBy, out bool rolledBack, out string? error)
    {
        rolledBack = false;
        error = null;
        if (!Guid.TryParse(childId, out var cg))
        {
            error = "invalid_child_id";
            return false;
        }
        return TryRollbackPolicyToLastKnownGood(new ChildId(cg), requestedBy, out rolledBack, out error);
    }
}
