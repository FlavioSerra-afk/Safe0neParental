using System;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

// Endpoint-facing compatibility helpers.
// These methods exist so Program.cs can call stable names even as we split JsonFileControlPlane into domain partials.
public sealed partial class JsonFileControlPlane
{
    /// <summary>
    /// Revoke a paired device token while preserving the paired device record.
    /// Returns true if the operation executed; 'revoked' indicates whether a device token was actually revoked.
    /// </summary>
    public bool TryRevokeDeviceToken(Guid deviceId, out bool revoked, out string? error)
    {
        // Canonical implementation lives in Tokens partial; this is an endpoint-facing wrapper.
        return TryRevokeDeviceToken_Internal(deviceId, "system", null, out _, out revoked, out error);
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
    /// Endpoint-facing token revoke signature used by Program.cs.
    /// Preserves the device record but invalidates the current token.
    /// </summary>
    public bool TryRevokeDeviceToken(Guid deviceId, string revokedBy, string? reason, out ChildId? childId)
    {
        childId = null;

        var ok = TryRevokeDeviceToken_Internal(deviceId, revokedBy, reason, out var owner, out var revoked, out _);
        if (!ok)
        {
            return false;
        }

        if (!revoked)
        {
            return false;
        }

        childId = owner;
        return childId is not null;
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
}
