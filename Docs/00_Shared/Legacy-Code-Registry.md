# Legacy Code Registry

This document lists **temporary compatibility code** that should be removed once migration to canonical APIs/contracts is complete.

> Rule: If you add a compat shim, you must add an entry here with a clear removal condition.

## Registry

| Area | File / Type | Why it exists | Canonical replacement | Remove when |
|---|---|---|---|---|
| Shared.Contracts | `LegacyAliases*.cs` | Back-compat type/ctor/shape aliases to keep older callers/tests building during contract migrations. | Canonical contract types in `Safe0ne.Shared.Contracts` (non-legacy). | All call sites updated and CI green with no `LegacyAliases*` references. |
| DashboardServer ControlPlane | `JsonFileControlPlane.EndpointCompat*.cs` | Endpoint parameter/shape shims (string/Guid parsing, older route shapes). | Canonical ControlPlane methods in domain partials. | All endpoints use canonical signatures and no more string overloads are referenced. |
| DashboardServer ControlPlane | `*.Compat*.cs` (if present) | Transitional wrappers while splitting `JsonFileControlPlane` into domain partials. | Domain partials (Policy/Tokens/Pairing/etc.). | Wrapper methods no longer referenced. |
| DashboardServer Local API | `src/Safe0ne.DashboardServer/Program.cs` (`POST /api/local/children`) | Accept legacy JSON body `{ name: "..." }` while canonical is `{ displayName: "..." }` to avoid breaking older tests/callers. | Canonical Local API contract uses `displayName` only. | All callers updated to `displayName` and `POST /api/local/children` no longer receives `name` in telemetry/tests. |
| DashboardServer Legacy v1 API | `src/Safe0ne.DashboardServer/Program.cs` (`GET /api/v1/children/{id}/status`) | Prevent UI polling spam by returning `200` with `data:null` when no heartbeat exists (instead of 404). | Canonical status polling via `/api/local/children/{id}/status`. | UI migrated fully to `/api/local/.../status` and v1 polling removed. |

## Tag convention

In code, use:

```csharp
// LEGACY-COMPAT: <why> | remove when <condition>
```

and prefer grouping shims in `*Compat*` / `Legacy*` files.
