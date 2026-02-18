using System;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

// Compat overloads to avoid signature-churn during ControlPlane partial refactors.
// This file is intentionally small and additive.
public sealed partial class JsonFileControlPlane
{
    /// <summary>
    /// Back-compat overload for callers that still pass device id as a string (e.g., route values).
    /// Parses to Guid and delegates to the canonical Guid-based overload.
    /// </summary>
    public bool TryRevokeDeviceToken(string deviceId, string revokedBy, string? reason, out ChildId? childId)
    {
        childId = null;
        if (!Guid.TryParse(deviceId, out var gid))
        {
            return false;
        }
        return TryRevokeDeviceToken(gid, revokedBy, reason, out childId);
    }
}
