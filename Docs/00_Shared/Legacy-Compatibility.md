# Legacy Compatibility Policy

This project is mid-migration from earlier “single-file / monolith” implementations to **domain-partial** implementations (ControlPlane, Contracts, etc.). During that migration we sometimes keep **compatibility shims** so older call-sites (or tests) continue to compile while we move consumers to the canonical API.

## Definitions

### Canonical API
The preferred, stable API surface that new code must use.

### Legacy compat / shim
Temporary code that exists **only** to keep older call-sites compiling and to avoid large, risky rewrites in a single patch.

## Required labeling

Any compat/shim must be clearly labeled:

* **File name** should include `Legacy`, `Compat`, or `EndpointCompat` where practical.
* **Header comment** at the top of the file:

```csharp
// LEGACY-COMPAT: Temporary compatibility shim.
// TODO(LEGACY-REMOVE): Remove once all call-sites are migrated to the canonical API.
```

* **Tracking tag**: every shim must contain `TODO(LEGACY-REMOVE)` so we can find and remove them with a single search.

## Design rules

1. **No new features in shims.** Features go into canonical code.
2. Shims should be **thin adapters** that delegate to canonical implementations.
3. Prefer **overloads** or **extension methods** (when appropriate) over duplicating logic.
4. Avoid creating *multiple* shims for the same concept—keep **one single source of truth**.

## Removal criteria

A shim can be removed when:

* All call-sites compile against the canonical API, **and**
* Tests cover the canonical path (or the shim is no longer referenced).

When removing, delete the shim file and update ADRs/SSOT if the canonical surface changed.
