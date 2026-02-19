# ADR-0004: Split JsonFileControlPlane into domain partials

## Status

Accepted

## Context

`JsonFileControlPlane` has grown large and mixes responsibilities (policy, tokens, heartbeat ingestion, activity, alerts, etc.). This increases merge conflicts, makes navigation difficult, and encourages accidental duplication.

We are already moving toward a modular architecture; the ControlPlane should match that approach.

## Decision

We will split `JsonFileControlPlane` into **partial class files by domain**.

### Naming convention

* `JsonFileControlPlane.Policy*.cs` — child policy, settings profiles, rollback, watchdog.
* `JsonFileControlPlane.Tokens*.cs` — device token generation, TTL, revoke helpers.
* `JsonFileControlPlane.Heartbeat*.cs` — ingest heartbeat, update snapshots, derive alerts/activity.
* `JsonFileControlPlane.WebFilter*.cs` — web filter rules, category matching, web activity/alerts.
* `JsonFileControlPlane.Persistence*.cs` — JSON read/write, atomic writes, schema/versioning.
* `JsonFileControlPlane.EndpointCompat*.cs` — temporary endpoint shims (see Legacy policy).

### Current implementation status

The split is being applied incrementally. As of the latest green build, these domain partials exist:

* `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.Children.cs`
  * Child list + archive state
  * Local child meta + last-known location
  * Child create/update helpers
* `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.Alerts.cs`
  * Local Settings Profile JSON blob (includes alerts routing + inbox state)
* `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.Requests.cs`
  * Access requests + grants (K8/P11)
* `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.Serialization.cs`
  * Load/seed/persist + state records (schema/versioning)

Planned next extractions (no API signature changes):

* `JsonFileControlPlane.Policy*.cs`
* `JsonFileControlPlane.Tokens*.cs`
* `JsonFileControlPlane.Heartbeat*.cs`
* `JsonFileControlPlane.Activity*.cs`
* `JsonFileControlPlane.Reports*.cs`

### Guardrails

* Each partial is allowed to define helpers for its domain **only**.
* Shared helpers must move to a dedicated `JsonFileControlPlane.Common.cs` (single source of truth).
* Compatibility shims must follow `Docs/00_Shared/Legacy-Compatibility.md`.

## Consequences

* Improved readability and lower merge conflict risk.
* More files, but easier navigation and safer refactors.
* Migration must be incremental—avoid “big bang” rewrites.


## Implementation status


### CP01-A (completed)
- Extracted persistence/serialization implementation from `JsonFileControlPlane.cs` into `JsonFileControlPlane.Serialization.cs`.
- Left endpoint/public surface methods in-place (no signature changes).
- Reserved domain partials (`Children`, `Requests`, `Alerts`, `Activity`) as stubs for future extraction.

**Files**
- `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.Serialization.cs` (new, active)
- `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.cs` (reduced; delegates to Serialization partial)
- `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.{Children,Requests,Alerts,Activity}.cs` (stubs)
