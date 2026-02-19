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
* `JsonFileControlPlane.Activity.cs` — append/read local activity log and export helpers.
* `JsonFileControlPlane.Serialization*.cs` — JSON read/write, atomic writes, schema/versioning.
* `JsonFileControlPlane.Children*.cs` — child CRUD + compat shims.
* `JsonFileControlPlane.Alerts*.cs` — alerts append/read/ack and export.
* `JsonFileControlPlane.Requests*.cs` — request/shortcut workflows (parent<->child).
* `ReportsDigest` now lives under `src/Safe0ne.DashboardServer/Reports/ReportsDigest.cs` (outside ControlPlane) and uses ControlPlane SSOT as its backing store.
* `JsonFileControlPlane.Tokens*.cs` — device token generation, TTL, revoke helpers.
* `JsonFileControlPlane.EndpointCompat*.cs` — temporary endpoint shims (see Legacy policy).

### Guardrails

* Each partial is allowed to define helpers for its domain **only**.
* Shared helpers must move to a dedicated `JsonFileControlPlane.Common.cs` (single source of truth).
* Compatibility shims must follow `Docs/00_Shared/Legacy-Compatibility.md`.

## Consequences

* Improved readability and lower merge conflict risk.
* More files, but easier navigation and safer refactors.
* Migration must be incremental—avoid “big bang” rewrites.