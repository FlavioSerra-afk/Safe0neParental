# Change Checklist (Docs-first LAW)

Updated: 2026-02-19

This checklist is **mandatory** for every patch ZIP. It exists to prevent SSOT drift between:
- code (truth for runtime),
- Docs (LAW / plan / contracts),
- User Manual (what users can do today).

## Required updates per patch

### A) Always (every patch)
- `Docs/00_Shared/Feature_Registry.md`
  - Update the rows for every domain/feature touched.
  - Evidence must include **real file paths** (and endpoint routes if relevant).
- `Docs/00_Shared/Patch_Workflow.md`
  - Add/adjust any workflow rules learned (only if rules changed).
- `Docs/00_Shared/Legacy-Code-Registry.md`
  - If any compat shim/alias/old route is added/changed, register it.

### B) If you changed any contracts / DTOs / endpoints
- `Docs/00_Shared/Contracts.md`
  - Canonical request/response shapes + compat rules.
- `Docs/00_Shared/SSOT.md`
  - SSOT boundaries, persistence rules, and storage prohibitions (e.g., no child/profile SSOT in localStorage).

### C) If you changed ControlPlane structure
- `Docs/00_Shared/ADRs/ADR-0004-ControlPlane-Partials-By-Domain.md`
  - Update extracted partial list + the "public surface stability" rule.

### D) If a user-visible behavior changed
- `Docs/90_User_Manual/*`
  - Update the relevant section(s).
  - Manual must describe **current behavior only** (no future promises).

## Patch Notes (required)
Every patch ZIP must include `PATCH_NOTES.md` at repo root containing:
- Summary (what + why)
- Files changed (paths)
- How to apply (extract at repo root)
- Post-apply checks:
  - rebuild
  - tests
  - F5 run (if relevant)
  - key UI flows / endpoints touched

## Definition of Done (per patch)
A patch is done only when:
- Build is green,
- Tests are green,
- Docs registry/SSOT/manual updated as applicable,
- No duplicate logic introduced (canonical-first + thin compat shims only).
