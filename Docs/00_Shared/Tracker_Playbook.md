# Tracker Playbook (How to keep Docs + manual aligned)

Updated: 2026-02-19

This repository uses **Docs-first** development. Docs are LAW.

## Sources of truth

### Runtime truth
- The running implementations in:
  - `src/Safe0ne.DashboardServer` (Local SSOT / APIs)
  - `src/Safe0ne.ParentApp` (Parent UX)
  - `src/Safe0ne.ChildAgent` (Kid agent + Kid UX)
  - `src/Safe0ne.Shared.Contracts` (canonical contracts)

### Planning + tracking truth
- `Docs/00_Shared/Feature_Registry.md` is the **SSOT for status** (✅ / 🟡 / 🔴).
- ADRs describe architectural decisions and boundaries.

### User-facing truth
- `Docs/90_User_Manual/*` describes what a user can do **today**.

## Status meanings (Feature Registry)
- ✅ Implemented: end-to-end wired, persisted in SSOT where required, and test-covered for core path.
- 🟡 Partial: partially wired, missing persistence/agent/UI leg, or only happy-path.
- 🔴 Not implemented: no meaningful implementation exists.
- 🧪 Stub/Demo (when used): present but explicitly non-SSOT, not production behavior.

## Evidence rules
For each Feature Registry row, include:
- code evidence: file paths + key classes/methods
- API evidence (if applicable): route(s) and request/response shapes
- tests evidence (if applicable): test file(s) verifying behavior

## Canonical-first rule (anti-churn)
- One canonical implementation per behavior.
- Legacy/compat surfaces must be **thin shims** forwarding to canonical logic.
- Compat shims must be registered in `Docs/00_Shared/Legacy-Code-Registry.md` with a removal condition.

## ControlPlane modularization rule
When splitting `JsonFileControlPlane`:
- Do **not** change endpoint-facing/public method signatures during extraction.
- Extract helpers first, then migrate call sites, then (only if needed) adjust public surface with compat shims + tests.
