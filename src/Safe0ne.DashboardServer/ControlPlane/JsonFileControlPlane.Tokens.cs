// Tokens domain entry point.
//
// ADR-0004: Split JsonFileControlPlane into domain partials.
//
// Canonical implementations now live in:
//  - JsonFileControlPlane.Tokens.Common.cs (shared token helpers)
//  - JsonFileControlPlane.Tokens.Revoke.cs (canonical revoke behavior)
//
// Any temporary endpoint-facing overloads must stay in EndpointCompat partials and be logged
// in Docs/00_Shared/Legacy-Code-Registry.md.

namespace Safe0ne.DashboardServer.ControlPlane;

public partial class JsonFileControlPlane
{
}
