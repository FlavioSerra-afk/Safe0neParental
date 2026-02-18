// LEGACY-TODO: This file intentionally contains no executable members yet.
//
// We are splitting JsonFileControlPlane into domain-focused partials.
// During the transition, some token helpers (GenerateDeviceToken, GetDeviceTokenTtl,
// ComputeSha256Hex, etc.) still live in JsonFileControlPlane.cs to avoid churn and
// accidental API surface changes.
//
// Once call-sites are consolidated and tests are stable, move the canonical implementations
// here and delete the legacy copies.

namespace Safe0ne.DashboardServer.ControlPlane;

public partial class JsonFileControlPlane
{
}
