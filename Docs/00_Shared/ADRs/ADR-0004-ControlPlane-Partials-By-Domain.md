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

### Guardrails

* Each partial is allowed to define helpers for its domain **only**.
* Shared helpers must move to a dedicated `JsonFileControlPlane.Common.cs` (single source of truth).
* Compatibility shims must follow `Docs/00_Shared/Legacy-Compatibility.md`.

## Consequences

* Improved readability and lower merge conflict risk.
* More files, but easier navigation and safer refactors.
* Migration must be incremental—avoid “big bang” rewrites.
