# Safe0neParental — SSOT Canvas + Tracker (Docs are LAW)

Updated: 2026-02-20

## Scope

Repo contains **Parent App (Windows)** + **Child Agent (Windows)** + **DashboardServer (Local Control Plane + Web UI)** + **Shared.Contracts** + **tests**.

This canvas is the single place to track:

- what is implemented in code now
- what is planned in `/Docs`
- what is missing / partial / stubbed
- the next safest slice order to move everything to 🟢.

## Status legend

- 🟢 Implemented
- 🟡 Partial
- 🔴 Not implemented
- 🧪 Stub (present but non-functional / not wired)

## Risk / Fragility tags

- ⚠️ Overload churn risk
- ⚠️ Legacy surface risk
- ⚠️ Cross-app contract risk
- ⚠️ Persistence/serialization risk
- ⚠️ UI regressions risk

---

## Architecture SSOT (Canonical surfaces today)

### Control Plane SSOT (DashboardServer)

Canonical store: `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.cs`

Partial-by-domain trend (in-flight):

- `JsonFileControlPlane.Policy.cs`
- `JsonFileControlPlane.PolicyRollback.cs`
- `JsonFileControlPlane.EndpointCompat*.cs` (compat shims)
- `JsonFileControlPlane.CryptoAndTokens.cs` (token helpers)
- `JsonFileControlPlane.Tokens.cs` (split by domain)

### Web UI modularization (DashboardServer)

Router kept thin + feature modules:

- `src/Safe0ne.DashboardServer/wwwroot/app/router.js` (hot file; minimize edits)
- `src/Safe0ne.DashboardServer/wwwroot/app/features/*` (feature modules)
- `src/Safe0ne.DashboardServer/wwwroot/app/module-registry.js`

### Shared contracts (Parent↔Server↔Child)

`src/Safe0ne.Shared.Contracts/*` (DTOs + policy surface)

Legacy aliases exist (must be tracked):

- `LegacyAliases*.cs`

---

## Known hotspots (exact paths)

### Device token revoke overload churn ⚠️ Overload churn risk ⚠️ Legacy surface risk

Canonical internal revoke invalidates token while keeping device record.

- `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.EndpointCompat.cs`
- `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.EndpointCompat.RevokeStringOverload.cs`
- `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.Tokens.Revoke.cs`
- `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.RevokeCompat.cs` (placeholder)
- `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.RevokeStringGuidOverloads.cs` (compiled out)

### Tokens domain split is incomplete ⚠️ Overload churn risk ⚠️ Persistence/serialization risk

- `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.CryptoAndTokens.cs`
- `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.Tokens.cs`

### Policy rollback + endpoint compat ⚠️ Legacy surface risk ⚠️ Cross-app contract risk

Canonical rollback surface locked; legacy overloads forward to canonical.

LKG snapshot written on Local Mode policy PUT/PATCH (`lastKnownGood.profile`).

- `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.PolicyRollback.cs`
- `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.EndpointCompat.cs`
- `src/Safe0ne.DashboardServer/Program.cs` (local policy writes + rollback endpoint)

### UI hot file overwrite risk ⚠️ UI regressions risk

- `src/Safe0ne.DashboardServer/wwwroot/app/router.js`

---

## Feature tracker (grouped by domain)

### 1) Foundation / SSOT

- 🟢 Local Control Plane file-backed SSOT
  - Lives: `JsonFileControlPlane.cs`, `Program.cs`
  - Acceptance: persists children/policies/requests; health endpoint works; schema migration non-breaking.
  - Risks: ⚠️ Persistence/serialization risk

- 🟢 SSOT Purity enforcement (UI storage discipline)
  - Lives: `wwwroot/app/features/children.js`, `wwwroot/app/features/devtools.js`, `wwwroot/app/modules.js`, `Docs/00_Shared/SSOT.md`
  - Acceptance: localStorage used for prefs only; no children/profile/policy persistence; offline mode does not claim saves; in-memory drafts allowed; DevTools SSOT Purity self-test detects and can purge forbidden keys.
  - Risks: ⚠️ UI regressions risk

- 🟢 Shared contracts baseline (v1)
  - Lives: `src/Safe0ne.Shared.Contracts/*`
  - Acceptance: additive-only; Parent+Child build against same DTOs.
  - Risks: ⚠️ Cross-app contract risk

### 2) Parent App (Windows)

- 🟢 Shell + navigation scaffold
  - Lives: `src/Safe0ne.ParentApp/*`
  - Acceptance: app boots; loads DashboardServer UI in WebView2.

- 🟢 Children list/cards + Add Child modal + avatar
  - Lives: Web UI module `wwwroot/app/features/children.js` + endpoints in `Program.cs`
  - Acceptance: list renders; add child works; avatar displayed.
  - Risks: ⚠️ UI regressions risk

- 🟢 Child Profile tabs (Policies / Alerts / Requests / Reports)
  - Lives: `children.js` module
  - Acceptance: tabs render; routes stable; async-safe.

- 🟢 DevTools unlock + toggles
  - Lives: `wwwroot/app/features/devtools.js`
  - Acceptance: 7x-tap unlock; toggles persist; hides again.

- 🟢 Requests inbox + approve/deny loop
  - Lives: `wwwroot/app/features/requests.js` + endpoints `/api/v1/requests/*`
  - Acceptance: approve/deny persists; grant created; kid observes grants.

- 🟢 Alerts inbox + routing + ack
  - Lives: `wwwroot/app/features/alerts.js` + alert endpoints in `Program.cs` + CP storage.
  - Acceptance: grouping+filters; ack persisted.

- 🟢 Reports scheduling (local digest)
  - Lives: `wwwroot/app/features/reports.js` + local endpoints in `Program.cs` + scheduler `Reports/ReportSchedulerService.cs`
  - Acceptance: schedule authoring persists to SSOT; run-now creates report_digest activity; background tick runs best-effort; `reportsState` updates without bumping `policyVersion`.
  - Risks: ⚠️ Persistence/serialization risk

- 🟡 Devices: pairing UX + device list
  - Lives: endpoints in `Program.cs` (`/pair/start`, `/pair/complete`, `/devices`) + CP device registries
  - Acceptance: pairing completes; device appears under child; token auth required when devices exist.

- 🟢 Diagnostics bundles (request + upload + download)
  - Lives: `Program.cs` (`/api/v1/children/{id}/diagnostics/bundles*`), Child Agent `Diagnostics/DiagnosticsBundle.cs` + command handler in `HeartbeatWorker.cs`, UI `wwwroot/app/features/support.js` and `children.js`.
  - Acceptance: parent can request bundle; kid uploads privacy-first ZIP; parent can download from Devices tab or Support page.

- 🟡 Device health surfaces
  - Lives: heartbeat endpoint + `ChildAgentStatus` contract + UI surfaces
  - Acceptance: last-seen + status badges; basic health indicators without regressions.

- 🟢 Audit log viewer
  - Lives: `src/Safe0ne.DashboardServer/ControlPlane/JsonFileControlPlane.Audit.cs` + `Program.cs` (`/api/local/children/{id}/audit`) + UI `wwwroot/app/features/admin.js`.
  - Acceptance: append-only chain per child; local endpoint returns envelope; policy PUT/PATCH and device revoke/unpair append entries.

### 3) Child Agent (Windows)

- 🟢 Pairing enrollment flow (minimal)
  - Lives: `src/Safe0ne.ChildAgent/Pairing/EnrollmentService.cs`, `AgentAuthStore.cs`
  - Acceptance: enrolls; stores device token; uses header on calls.

- 🟢 Requests creation + offline queue (baseline)
  - Lives: `src/Safe0ne.ChildAgent/Requests/AccessRequestQueue.cs`
  - Acceptance: queues when offline; flushes later.

- 🟢 Geofence evaluation + events
  - Lives: `src/Safe0ne.ChildAgent/Location/GeofenceEvaluator.cs` + sender
  - Acceptance: detects enter/exit; emits activity events; parent shows alerts/activity.

- 🟡 Policy sync hardening (version/fingerprint/watchdog)
  - Lives: `Policy/PolicyCacheStore.cs`, `HeartbeatWorker.cs` + server watchdog logic
  - Acceptance: agent reports applied version; server detects overdue; rollback endpoint works.
  - Risks: ⚠️ Cross-app contract risk

- 🟢 Screen time tracking / enforcement
  - Lives: `ScreenTime/*` + schedule evaluator `ChildUx/ScheduleHelper.cs`
  - Acceptance: tracks minutes; enforces configured daily limit; grace+warnings honored; **schedule windows (Bedtime + School + Homework) are authored in Parent profile and enforced via effective mode**.

- 🟢 App usage tracking / per-app limits
  - Lives: `AppUsage/*`
  - Acceptance: tracks app usage; enforces per-app caps; blocked UX + request loop best-effort.

- 🟢 Web filter enforcement + circumvention best-effort
  - Lives: `WebFilter/*`
  - Acceptance: blocks domains/categories; logs attempts; circumvention signals to alerts + reports.

- 🟡 Kid UX (Today / Block screens / Why + Request)
  - Lives: `ChildUx/*`
  - Acceptance: renders child-facing UI; shows reason; offers request action.

---

## Next slices queue (ranked)

1) **Stability Wave:** Canonicalize revoke/token/rollback surfaces
   - Goal: eliminate signature collisions + ensure single canonical API per behavior.
   - Touch: `JsonFileControlPlane.EndpointCompat*.cs`, `CryptoAndTokens.cs`, `Tokens.cs`, `/Docs/00_Shared/Legacy-Code-Registry.md`.

2) **Reports:** make scheduling truly functional end-to-end
   - Register hosted service, ensure SSOT keys stable, add marker test.

3) **Device pairing hardening**
   - Tighten token TTL/revoke; ensure consistent `childId/deviceId` mappings; add tests.

4) **Kid enforcement completion (incremental)**
   - Screen time → per-app → web filter.

5) **Migration cleanup**
   - Remove compiled-out/no-op compat files when registry indicates safe.

---

## Legacy / Compat (policy summary)

Canonical-first: implement behavior once.

Legacy shims are thin adapters only, tagged:

```csharp
// LEGACY-COMPAT: <reason> | RemoveAfter: <milestone> | Tracking: <issue-id-or-doc-section>
// TODO(LEGACY-REMOVE): <explicit condition>
```

Every shim must be listed in `/Docs/00_Shared/Legacy-Code-Registry.md`.

---

## Patch log

- 16W29: EPIC-ACTIVITY moved to 🟢 (UI Export button wired to Local API export envelope; retention already enforced in SSOT).
- 26W08: Diagnostics bundles surfaced per-child (Devices tab) + troubleshooting doc updated.
