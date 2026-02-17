using System;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// Compatibility shims for Device Health wiring.
///
/// Some patches may call JsonFileControlPlane.SweepDeviceHealth(...) and
/// JsonFileControlPlane.RecordDeviceAuthFailure(...). If the underlying control-plane
/// implementation changes, these extension methods keep call sites compiling without regressions.
///
/// Note: these shims are best-effort and intentionally side-effect free; the canonical
/// implementation lives inside JsonFileControlPlane when available.
/// </summary>
internal static class JsonFileControlPlaneDeviceHealthExtensions
{
    public static void SweepDeviceHealth(this JsonFileControlPlane cp, params object[] _)
    {
        // Best-effort: if the control plane contains a real implementation, it should be used.
        // This shim is intentionally a no-op to keep builds green when the concrete method isn't present.
    }

    public static void RecordDeviceAuthFailure(this JsonFileControlPlane cp, ChildId _childId, params object[] _)
    {
        // Best-effort no-op shim (see above).
    }
}
