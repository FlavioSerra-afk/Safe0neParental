# ADR-0003 — Modular DashboardServer UI feature modules

Status: **Accepted**  
Updated: 2026-02-04

## Context
Safe0ne Parental uses a ZIP-first patch workflow where patches are applied by extracting a ZIP at repo root. This is convenient, but it has an important failure mode:

- Large “hot files” (notably `src/Safe0ne.DashboardServer/wwwroot/app/router.js`) are touched by many patches.
- If a patch is produced from a repo snapshot that is missing a previously implemented change, applying the patch can overwrite the hot file and silently **regress** features.

We need a structure that:
- Minimizes hot-file edits.
- Supports independent feature evolution (alerts, requests, support, reports).
- Enables/disable features centrally (feature flags) for safer rollout.
- Keeps performance acceptable for a Windows-first, local DashboardServer UI.

## Decision
Adopt a **modular UI architecture** for the DashboardServer web UI:

1) Keep `router.js` **thin**:
- route parsing + page mounting
- invoking stable “hooks” that modules can register

2) Move feature logic into modules:
- `src/Safe0ne.DashboardServer/wwwroot/app/core/*` for shared utilities
- `src/Safe0ne.DashboardServer/wwwroot/app/features/*` for feature areas

3) Add a small master bootstrap/flags mechanism:
- Feature modules expose `init(ctx)` and optional hooks (e.g., `decorateAlerts(alerts, ctx)`).
- A central `feature_flags` config controls which modules are enabled.

4) Add regression guardrails:
- Maintain “feature markers” for hot files and/or a lightweight test that asserts markers exist.

## Consequences
### Positive
- Dramatically reduces risk of regressions caused by ZIP extraction overwriting a hot file.
- Smaller, safer patches: often one module file per feature.
- Cleaner ownership boundaries (alerts vs requests vs support).
- Optional feature flags provide controlled rollout and quick disable switches.

### Negative / trade-offs
- Adds a small number of additional JS files (module files).
- Requires discipline: avoid creating a new file for every tiny patch; prefer a bounded module set.

### Performance notes (guidance)
- Keep the number of eagerly loaded modules modest (alerts/requests/support/etc.).
- Prefer splitting by feature/route boundaries rather than many micro-files.
- If performance becomes a concern, consider preloading or bundling later; the modular structure keeps that option open.

## References
- AWS Prescriptive Guidance: ADRs are immutable once accepted; new decisions supersede old ones.  
- UK GDS Way: ADR lifecycle and “Superseded” marking when replaced.
