# Legacy Compatibility Policy

Updated: 2026-02-18

This project is mid-migration from earlier “single-file / monolith” implementations to **canonical SSOT + modular domains**.

Legacy must never become “parallel SSOT”. It exists only to avoid breaking older persisted data or older agent/app call-sites while we migrate.

---

## Definitions

### Canonical API
The preferred, stable API surface that new code must use. Canonical behavior must be implemented **once**.

### Legacy compat / shim
Temporary code that exists only to:
- accept older shapes/keys/names and map them to canonical
- preserve older call-sites while migration is underway

**A legacy shim must forward to canonical logic.** No duplicated behavior.

---

## Allowed legacy

### ✅ Back-compat mapping (healthy)
- Accept older persisted keys/shapes, map to canonical SSOT.
- Deterministic mapping only.

### ⚠️ Compat endpoints (facade-only)
- `/api/v1/*` may exist temporarily.
- Must call the same canonical ControlPlane methods as `/api/local/*`.

---

## Disallowed legacy

### 🚫 Alternate SSOT
- UI persisting children/profiles/policies/etc. into localStorage.

LocalStorage is for **preferences only**.

---

## Required annotations
C#:
```csharp
// LEGACY-COMPAT: <reason> | RemoveAfter: <milestone> | Tracking: <Docs section / issue>
// TODO(LEGACY-REMOVE): <explicit condition>
```
JS:
```js
// LEGACY:REMOVE_AFTER(<condition>)
```

---

## Deprecation / removal checklist (required)
A legacy shim may be removed only when:
1. All call-sites are migrated to canonical.
2. Stored-data compatibility is proven (migration evidence or mapping tests).
3. Tests cover canonical behavior.
4. The entry is removed from `Legacy-Code-Registry.md`.

---

## Registry (required)
All legacy shims must be listed in:
- `Docs/00_Shared/Legacy-Code-Registry.md`
