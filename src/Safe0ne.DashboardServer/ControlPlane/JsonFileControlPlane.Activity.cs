// LEGACY-COMPAT: Temporary placeholder to avoid duplicate Activity members during CP01 refactor rollback.
// RemoveAfter: CP01 re-attempt with verified partial extraction.
// Tracking: Docs/00_Shared/Legacy-Code-Registry.md#controlplane-partials

namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// Activity logic currently lives in JsonFileControlPlane.cs (canonical).
/// This file intentionally contains no members to avoid duplicate definitions.
/// </summary>
public partial class JsonFileControlPlane
{
}
