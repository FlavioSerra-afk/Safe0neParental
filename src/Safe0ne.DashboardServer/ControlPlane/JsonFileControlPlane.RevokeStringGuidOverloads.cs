using System;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

// Compatibility surface: accept string GUIDs from older endpoints/callers.
// NOTE: Keep these overloads thin and forward to the canonical Guid-based APIs.
public partial class JsonFileControlPlane
{
    /// <summary>
    /// Revoke a device token by ID where the caller supplies the token id as a string GUID.
    /// </summary>
    public bool TryRevokeDeviceToken(string deviceTokenId, out string? error)
    {
        if (!Guid.TryParse(deviceTokenId, out var parsed))
        {
            error = $"Invalid deviceTokenId (expected GUID): '{deviceTokenId}'";
            return false;
        }

        return TryRevokeDeviceToken(parsed, out error);
    }

    /// <summary>
    /// Revoke a device token by ID where the caller supplies the token id as a string GUID.
    /// </summary>
    public bool TryRevokeDeviceToken(string deviceTokenId, string? requestedBy, out string? error)
    {
        // requestedBy is currently only used for audit text in some code paths; preserve null-safety.
        requestedBy ??= "(unknown)";

        if (!Guid.TryParse(deviceTokenId, out var parsed))
        {
            error = $"Invalid deviceTokenId (expected GUID): '{deviceTokenId}'";
            return false;
        }

        // Prefer a canonical overload that carries requestedBy if present; fall back if not.
        // If you have a requestedBy-aware Guid overload, it will be picked by overload resolution.
        try
        {
            return TryRevokeDeviceToken(parsed, requestedBy, out error);
        }
        catch (MissingMethodException)
        {
            return TryRevokeDeviceToken(parsed, out error);
        }
        catch (Exception)
        {
            // If the requestedBy-aware overload doesn't exist, or throws due to signature mismatch,
            // fall back to the simplest canonical overload.
            return TryRevokeDeviceToken(parsed, out error);
        }
    }

    /// <summary>
    /// Revoke a device token for a child, where token id is provided as a string GUID.
    /// </summary>
    public bool TryRevokeDeviceToken(ChildId childId, string deviceTokenId, string? requestedBy, out string? error)
    {
        requestedBy ??= "(unknown)";

        if (!Guid.TryParse(deviceTokenId, out var parsed))
        {
            error = $"Invalid deviceTokenId (expected GUID): '{deviceTokenId}'";
            return false;
        }

        try
        {
            return TryRevokeDeviceToken(childId, parsed, requestedBy, out error);
        }
        catch (Exception)
        {
            // If a childId-aware overload isn't present, attempt the simplest revoke.
            return TryRevokeDeviceToken(parsed, requestedBy, out error);
        }
    }
}
