# SSOT — Safe0ne Parental (Shared)

Updated: 2026-02-18

## Purpose
This document is **LAW**. It defines the **single source of truth (SSOT)** and the rules that keep Safe0ne maintainable while we ship features fast **without regressions**.

Safe0ne Parental is a cross-app system:
- **Parent App (Windows)**: config + approvals + reporting (WebView2 UI)
- **DashboardServer (Windows localhost)**: local-first API + ControlPlane SSOT + UI static hosting
- **Child Agent (Windows)**: enforcement + child UX
- **Shared.Contracts**: DTOs used across Parent↔Server↔Child

---

## Non-negotiables
1. **Docs-first**: `/Docs/**` is the plan; code must match.
2. **Green always**: every patch must build + tests green.
3. **Canonical-first**: one canonical implementation per behavior.
4. **Legacy is thin**: legacy shims forward to canonical and are tracked for removal.
5. **No parallel SSOT**: no alternate stores for domain data.

---

## SSOT invariants

### Canonical store
The canonical persisted state is the **ControlPlane SSOT**:
- `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane*.cs`

All domain mutations (children, profiles, policies, requests, alerts, reports, device registry) **must** flow through ControlPlane.

### Allowed local state
Local-only state is allowed only for:
- UI preferences (filters, expanded panels, selected tab)
- dev-only toggles (DevTools unlocked)

### Forbidden local state
The following must **never** be persisted as “fallback SSOT”:
- children list
- child profile / policy / geofences
- device registry / tokens
- requests / grants
- alerts / activity
- reports schedule

If Local API is unavailable, the UI must render:
- read-only skeleton or “Local service offline” state
- optional **DevTools-only Demo Mode** (explicitly non-SSOT, clearly labelled)

---

## LocalStorage policy (UI)

### Allowed key types
- `safe0ne_devtools_unlocked_v1`
- `safe0ne.theme.*` (if present)
- `safe0ne.ui.*` preference keys (filters/sorts/toggles)

### Disallowed keys
Any key that persists domain state (children/profiles/policy/etc.).

### Enforcement
DevTools must include a **Self-Test: SSOT Purity**:
- If `/api/local/_health` is reachable, then **no domain state keys** may be written to localStorage.
- A single allow-list of keys lives in code.

---

## “Legacy” — definition and allowed uses
Legacy exists in two forms. Only one is healthy.

### ✅ Allowed: Back-compat mapping (healthy)
Examples:
- Server mapping legacy policy keys/shapes to canonical policy objects
- Agent parsing legacy field names for older stored policy data

Rules:
- must be deterministic (no “best effort” guesses)
- must be **thin adapters**
- must be tracked in `Legacy-Code-Registry.md`

### ⚠️ Allowed but temporary: Compat facades (API)
Examples:
- `/api/v1/*` endpoints that forward to `/api/local/*` canonical logic

Rules:
- must call the same canonical ControlPlane methods
- must not implement separate behavior
- must not store data elsewhere

### 🚫 Not allowed: Alternate SSOT / behavior fallback
Example:
- UI persisting children/profiles into localStorage when Local API fails

This is the fastest way to create “UI != SSOT” bugs and must be removed.

---

## Canonical-first enforcement

### Canonical API rule
- Implement behavior **once**.
- Canonical surfaces are stable and referenced by endpoints.
- Legacy shims forward to canonical.

### Required tags
C#:
```csharp
// LEGACY-COMPAT: <reason> | RemoveAfter: <milestone> | Tracking: <Docs section / issue>
// TODO(LEGACY-REMOVE): <explicit condition>
```
JS:
```js
// LEGACY:REMOVE_AFTER(<condition>)
```

### Registry (required)
Every shim must be listed in:
- `Docs/00_Shared/Legacy-Code-Registry.md`

---

## Contracts evolution (anti-churn)

### Problem
Positional record DTO constructors + named args cause patch cascades:
- rename breaks compilation
- overloads collide

### Policy
- **Production contracts may remain positional records**, but tests/tools must not construct them using named args.
- Prefer one of:
  1) **Builder/factory** (recommended)
  2) A stable helper method per DTO (in tests)

Example policy:
- In tests: `ContractBuilders.Heartbeat(...)` (single place to update when fields evolve)

### Hygiene
Comments in `Shared.Contracts` must point to the actual canonical DTO type and never claim “authoritative lives here” when the file is a stub.

---

## Modularization rules (avoid spaghetti)

### DashboardServer UI modules (ADR-0003)
- Keep `wwwroot/app/router.js` minimal and stable.
- Implement features in `wwwroot/app/features/*.js`.
- Add to `module-registry.js`.

### ControlPlane partials by domain (ADR-0004)
- Split internals into domain partials **without changing endpoint-facing signatures**.
- Extract helpers first; keep the API surface stable.
- Never duplicate helpers across partials (avoid CS0111).

Recommended domain partial targets:
- `JsonFileControlPlane.Children.cs`
- `JsonFileControlPlane.Alerts.cs`
- `JsonFileControlPlane.Activity.cs`
- `JsonFileControlPlane.Reports.cs`
- `JsonFileControlPlane.Audit.cs`

---

## Required self-tests (DevTools)
DevTools must expose a diagnostics view that can run:
- **SSOT Purity** (localStorage allow-list)
- **Local API health** (`/api/local/_health`)
- **Compat facade sanity** (optional): `/api/v1/_health` delegates to same logic (if retained)

---

## Patch workflow (summary)
The authoritative workflow is in `Docs/00_Shared/Patch_Workflow.md`.

Key rule: **every change is a single ZIP patch** and must keep the repo green.
