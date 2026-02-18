# SSOT — Safe0ne Parental (Shared)

Updated: 2026-02-04

## Product
**Safe0ne Parental** is a cross-platform parental control system built as **two apps**:
- **Parent App**: configuration, approvals, reporting
- **Child App (Agent)**: enforcement + child-facing experience

Windows is first:
- Parent App: Windows UI via WebView2
- Child App: Windows agent/service + child UX

## Non-negotiable workflow

## UI modularization policy (DashboardServer UI)
**Goal:** reduce regression risk from ZIP extraction overwriting hot files (especially `router.js`) by isolating feature logic into stable modules.

**Decision log:** See ADR **ADR-0003 — Modular DashboardServer UI feature modules** (`/Docs/00_Shared/ADRs/ADR-0003-Modular-DashboardServer-UI-Modules.md`).

### Rules
- Prefer **feature modules** under: `src/Safe0ne.DashboardServer/wwwroot/app/features/`
- Keep `src/.../wwwroot/app/router.js` **thin** (route parsing + mounting + calling module hooks).
- Each feature change should ideally touch **one module file** (plus tests) rather than editing `router.js`.
- A **master bootstrap/feature-flags** file may enable/disable modules for controlled rollout.

### Recommended modules (DashboardServer UI)
Core:
- `core/api_client.js` (fetch wrappers + error handling)
- `core/state_store.js` (cache + refresh orchestration)
- `core/format_utils.js` (UTC parsing, duration formatting, CSV helpers)
- `core/feature_flags.js` (enable/disable feature modules)

Features:
- `features/alerts.js`
- `features/requests.js`
- `features/support.js`
- `features/reports.js`
- `features/policies.js`
- `features/children.js`

- Patches, fixes, and implementation steps are delivered as **ZIP patches** with full file paths.
- Every ZIP patch includes `PATCH_NOTES.md` (summary, file list, apply steps, post-apply checks).
- One patch at a time. Avoid manual edits unless unavoidable.

## Legacy compatibility (temporary shims)

During migrations we may keep temporary compatibility code **only** to prevent regressions while we move call-sites to the canonical API.

**Rules:**

1. Any shim **must** be labeled with `LEGACY-COMPAT` and include `TODO(LEGACY-REMOVE)`.
2. Shims must be **thin adapters** (no duplicated business logic).
3. New features must land in canonical code, not in shims.
4. When a canonical migration is complete, delete shims and update docs/ADRs.

See: `Docs/00_Shared/Legacy-Compatibility.md`.

## Core policy precedence (shared truth)
1) Always Allowed
2) Grants (time-boxed exceptions approved by parent)
3) Mode (Lockdown/Open/Homework/Bedtime)
4) Schedules & Budgets (bedtime/school/homework/daily limits)

## Sidebar rule
To keep the sidebar small: **all child policy controls live inside Child Profile** in the Parent App.



## Phase A (Stub completeness first)
Immediate priority is **end-to-end stubs** across Parent UI → Local SSOT → Local API → Kid agent awareness/logging.
Stubs must be visible in the UI, stored in SSOT with safe defaults, and wired through Local API (even if enforcement/analytics are placeholders).

## Planned policy surface (full model)
The shared policy surface (Windows-first, cross-platform compatible) includes:
- **Mode**: Open / Homework / Bedtime / Lockdown
- **Time budget**: daily minutes, warning thresholds, schedules
  - `policy.timeBudget.dailyMinutes`
  - `policy.timeBudget.graceMinutes` *(PATCH 16R, optional; default 0)*
  - `policy.timeBudget.warnAtMinutes` *(PATCH 16R, optional; default [5,1])*
  - `policy.timeBudget.schedules.*`
- **Routines**: bedtime/school/homework presets (templates)
- **Apps**: allow/deny list, per-app limits, block new apps (stubs)
- **Web**: category rules allow/alert/block, SafeSearch toggles (stubs), allow/deny domains
- **Exceptions**: time-boxed temporary grants (per app/site/category)
- **Always Allowed**: apps/sites/contacts that always work (emergency/school)
- **Location**: sharing on/off, geofences (stubs)
- **Alerts**: toggles/thresholds for alert generation (stubs)

## Derived state (stored in SSOT, additive)
Some values are not "policy" but are still stored in the Local Control Plane to avoid parallel registries and enable deterministic behavior.

- **locationState**
  - `geofences[]`: per-geofence inside/outside state and last transition
    - `{ id, inside, lastTransitionAtUtc }`
  - `lastEvaluatedAtUtc`: last evaluation timestamp

Geofence transitions are emitted as Activity events:
- `geofence_enter`
- `geofence_exit`

All additions must be **additive + backward compatible**. Old profiles/policies auto-migrate on read/write.

## UX baseline
- No jargon; plain language.
- Every restriction shows “why” and offers a request path.
- Every parent setting has a tooltip explanation and safe default.
