using System;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

public partial class JsonFileControlPlane
{
    /// <summary>
    /// Back-compat overload: endpoints sometimes pass deviceId as a string.
    /// Canonical storage uses Guid deviceId.
    /// </summary>
    public bool TryRevokeDeviceToken(string deviceId, string revokedBy, string? reason, out ChildId childId)
    {
        childId = default;

        if (string.IsNullOrWhiteSpace(deviceId))
            return false;

        if (!Guid.TryParse(deviceId, out var deviceGuid))
            return false;

        return TryRevokeDeviceToken(deviceGuid, revokedBy, reason, out childId);
    }
}
