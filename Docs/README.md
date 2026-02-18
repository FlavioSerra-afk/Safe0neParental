# Docs (LAW)

Updated: 2026-02-18

If you read only one thing first, read:
1. `00_Shared/SSOT.md` (SSOT laws + modularization + legacy rules)
2. `00_Shared/Patch_Workflow.md` (zip patch workflow)
3. `00_Shared/Feature_Registry.md` (what must exist)
4. `10_Parent_App/Implementation_Matrix.md` and `20_Child_App/Implementation_Matrix.md` (delivery tracker)

## ADRs (decisions)
- `00_Shared/ADRs/` (Local-first control plane, UI modules, ControlPlane partials, legacy deprecation)

## Product docs
- Parent App: `10_Parent_App/`
- Child App: `20_Child_App/`
- UI/UX spec pack: `30_UI_UX_Pack/`
- User manual: `90_User_Manual/`

## Where to add new docs
Prefer updating existing docs. Add new files only when:
- a rule cannot fit cleanly into `SSOT.md`, or
- a decision needs an ADR.

## Canonical indices
- SSOT laws: `00_Shared/SSOT.md`
- Legacy policy: `00_Shared/Legacy-Compatibility.md`
- Legacy inventory: `00_Shared/Legacy-Code-Registry.md`
