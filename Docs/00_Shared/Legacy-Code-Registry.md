# Legacy Code Registry

This document lists **temporary compatibility code** that should be removed once migration to canonical APIs/contracts is complete.

> Rule: If you add a compat shim, you must add an entry here with a clear removal condition.

## Registry

| Area | File / Type | Why it exists | Canonical replacement | Remove when |
|---|---|---|---|---|
| DashboardServer Local API | `Program.cs` `POST /api/local/children` accepts `{ name: "..." }` | Back-compat for older UI/tests that used `name` instead of `displayName` | `{ displayName: "..." }` | Remove once all callers use `displayName` and no persisted/automated flows depend on `name`. |
| Dashboard UI | DevTools SSOT purity self-test | Prevent SSOT drift via localStorage | Allowed-keys allowlist in DevTools | Keep permanently (not removed), but update allowlist when prefs keys change |
| Shared.Contracts | `LegacyAliases*.cs` | Back-compat type/ctor/shape aliases to keep older callers/tests building during contract migrations. | Canonical contract types in `Safe0ne.Shared.Contracts` (non-legacy). | All call sites updated and CI green with no `LegacyAliases*` references. |
| DashboardServer ControlPlane | `JsonFileControlPlane.EndpointCompat*.cs` | Endpoint parameter/shape shims (string/Guid parsing, older route shapes). | Canonical ControlPlane methods in domain partials. | All endpoints use canonical signatures and no more string overloads are referenced. |
| DashboardServer ControlPlane | `*.Compat*.cs` (if present) | Transitional wrappers while splitting `JsonFileControlPlane` into domain partials. | Domain partials (Policy/Tokens/Pairing/etc.). | Wrapper methods no longer referenced. |

## Tag convention

In code, use:

```csharp
// LEGACY-COMPAT: <why> | remove when <condition>
```

and prefer grouping shims in `*Compat*` / `Legacy*` files.
