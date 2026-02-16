# Patch Workflow (ZIP-first)

## Required format for every patch ZIP
- ZIP contains all changed/new files in correct folder paths.
- Include `PATCH_NOTES.md` at the root:
  - Summary
  - Files changed (paths)
  - How to apply
  - Post-apply checks

## Rules
- One patch at a time.
- Avoid manual edits; if unavoidable, include exact instructions.
- Keep contracts additive-only once coding begins.


## Hot-file guardrails (prevent accidental regressions)
Some files are frequently touched and easy to accidentally regress due to ZIP extraction overwriting (for example: `src/Safe0ne.DashboardServer/wwwroot/app/router.js`).

Before implementing a patch that changes a hot file:
- Confirm the repo ZIP snapshot you are patching **already contains** all previously verified changes (SSOT snapshot).
- Prefer implementing changes in a **feature module** (`wwwroot/app/features/*`) instead of editing the hot file.
- If the hot file must change, keep edits localized and preserve existing feature blocks.

### Marker checks (recommended)
Maintain a short list of “feature markers” that must remain present. If any marker is missing in the current SSOT snapshot, produce a **stabilization patch** first before adding new features.
