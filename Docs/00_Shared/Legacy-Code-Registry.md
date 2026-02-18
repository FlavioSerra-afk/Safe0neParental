# Legacy Code Registry

This document lists **temporary compatibility code** that should be removed once migration to canonical APIs/contracts is complete.

> Rule: If you add a compat shim, you must add an entry here with a clear removal condition.

## Registry

| Area | File / Type | Why it exists | Canonical replacement | Remove when |
|---|---|---|---|---|
| Dashboard UI | `wwwroot/app/features/children.js` localStorage fallback (`safe0ne.children.v1`, `safe0ne.childProfiles.v1`) | Previously used as offline fallback store for children/profiles | SSOT-only via Local API; localStorage prefs-only | Remove once UI no longer writes these keys (PATCH_16W27) |
| Dashboard UI | DevTools SSOT purity self-test | Prevent SSOT drift via localStorage | Allowed-keys allowlist in DevTools | Keep permanently (not removed), but update allowlist when prefs keys change |
| Shared.Contracts | `LegacyAliases*.cs` | Back-compat type/ctor/shape aliases to keep older callers/tests building during contract migrations. | Canonical contract types in `Safe0ne.Shared.Contracts` (non-legacy). | All call sites updated and CI green with no `LegacyAliases*` references. |
| DashboardServer ControlPlane | `JsonFileControlPlane.EndpointCompat*.cs` | Endpoint parameter/shape shims (string/Guid parsing, older route shapes). | Canonical ControlPlane methods in domain partials. | All endpoints use canonical signatures and no more string overloads are referenced. |
| DashboardServer ControlPlane | `*.Compat*.cs` (if present) | Transitional wrappers while splitting `JsonFileControlPlane` into domain partials. | Domain partials (Policy/Tokens/Pairing/etc.). | Wrapper methods no longer referenced. |
| DashboardServer Local API | `Program.cs` Local `POST /api/local/children` accepts `{ name: "..." }` | Older UI/tests payload used `name` instead of canonical `displayName`. | Canonical `CreateLocalChildRequest(DisplayName, ...)` semantics. | All callers updated to send `displayName` and legacy callers removed. |
| DashboardServer Local API | `Program.cs` Local `GET /api/local/children/{id}/activity/export` | Stable support/export envelope for activity without coupling UI to internal JSON. | SSOT-backed activity stream (`GetLocalActivityJson`). | Keep (additive). If replaced, only remove after a versioned replacement exists and UI/tests migrate. |

## Tag convention

In code, use:

```csharp
// LEGACY-COMPAT: <why> | remove when <condition>
```

and prefer grouping shims in `*Compat*` / `Legacy*` files.
