# Legacy Code Registry

This document lists **temporary compatibility code** that should be removed once migration to canonical APIs/contracts is complete.

> Rule: If you add a compat shim, you must add an entry here with a clear removal condition.

## Registry

| Area | File / Type | Why it exists | Canonical replacement | Remove when |
|---|---|---|---|---|
| Shared.Contracts | `LegacyAliases*.cs` | Back-compat type/ctor/shape aliases to keep older callers/tests building during contract migrations. | Canonical contract types in `Safe0ne.Shared.Contracts` (non-legacy). | All call sites updated and CI green with no `LegacyAliases*` references. |
| DashboardServer ControlPlane | `JsonFileControlPlane.EndpointCompat*.cs` | Endpoint parameter/shape shims (string/Guid parsing, older route shapes). | Canonical ControlPlane methods in domain partials. | All endpoints use canonical signatures and no more string overloads are referenced. |
| DashboardServer ControlPlane | `*.Compat*.cs` (if present) | Transitional wrappers while splitting `JsonFileControlPlane` into domain partials. | Domain partials (Policy/Tokens/Pairing/etc.). | Wrapper methods no longer referenced. |
| DashboardServer HTTP API | `Program.cs` – `GET /api/v1/children/{id}/status` | Legacy polling route used by older UI; returns 200 with `data:null` when no heartbeat to avoid console spam. | Canonical `/api/local/children/{id}/status` (or consolidated status service). | UI migrated to canonical and route removed. |
| DashboardServer HTTP API | `Program.cs` – `GET /api/local/children/{id}/reports/run-now` | Convenience/legacy method to avoid 405 churn; canonical is POST. | `POST /api/local/children/{id}/reports/run-now` | All callers use POST and tests updated. |

## Tag convention

In code, use:

```csharp
// LEGACY-COMPAT: <why> | remove when <condition>
```

and prefer grouping shims in `*Compat*` / `Legacy*` files.
