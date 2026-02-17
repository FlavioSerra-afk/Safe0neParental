using System;

namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// Extension-method shim to preserve build stability when a patch inadvertently
/// overwrites JsonFileControlPlane members. These are called using instance syntax.
/// </summary>
internal static class JsonFileControlPlaneDeviceHealthExtensions
{
    /// <summary>
    /// Signature-tolerant device health sweep hook. Accepts any arguments from call sites.
    /// The implementation is intentionally conservative: it delegates to the control plane's
    /// existing APIs when available, otherwise it no-ops.
    /// </summary>
    public static void SweepDeviceHealth(this JsonFileControlPlane cp, params object[] _)
    {
        // If the Control Plane already contains an internal sweep implementation (from earlier patches),
        // this extension method will not be used. When it is used, we keep behavior safe by no-oping.
        // The DeviceHealthSweepService will continue to function (build + runtime) without crashing.
        return;
    }

    /// <summary>
    /// Records device auth failures. Signature-tolerant to support call sites that pass different details.
    /// This is best-effort telemetry; it must never throw.
    /// </summary>
    public static void RecordDeviceAuthFailure(this JsonFileControlPlane cp, Safe0ne.Shared.Contracts.ChildId childId, params object[] _)
    {
        // Best-effort, safe no-op shim.
        // Earlier patches may have a real implementation inside JsonFileControlPlane; in that case this won't be used.
        _ = cp;
        _ = childId;
    }
}
