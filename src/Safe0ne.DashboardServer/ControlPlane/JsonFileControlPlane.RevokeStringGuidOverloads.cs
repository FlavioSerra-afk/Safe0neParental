// LEGACY-COMPAT (temporary)
//
// This file previously contained experimental overloads for TryRevokeDeviceToken that accepted
// string/Guid combinations. Those overloads caused signature drift and compile churn.
//
// Policy (Docs/Legacy-Registry.md):
//  - Prefer ONE canonical API surface.
//  - If compat is required, keep it in *one* place (JsonFileControlPlane.EndpointCompat.cs)
//    and keep it minimal + parse-only.
//
// This file is intentionally compiled as a no-op so any workspace that still references it
// will not break the build.

#if false
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

public sealed partial class JsonFileControlPlane
{
    // Intentionally blank.
}
#endif
