# Feature Registry (SSOT for feature tracking)

Updated: 2026-02-17  
Owner: Repo-wide (Docs-first LAW)

This document is the **single source of truth** for tracking feature delivery status across:

- **Parent App (Windows WPF + WebView2 UX)**
- **DashboardServer / Local Control Plane (SSOT)**
- **Kid Agent / Kid UX**

> Rule: We track **features**, not patches.  
> Status is recorded as ✅ / 🟡 / 🔴 and must be supported by **evidence** (UI surface + SSOT path + marker/self-test where applicable).

## Status legend
- ✅ **Implemented**: wired end-to-end, uses SSOT, does not regress tests, visible in UI where applicable.
- 🟡 **Partial**: UI or stubs exist, or only some platforms; missing wiring, enforcement, or end-to-end loop.
- 🔴 **Planned**: defined in docs but not yet implemented.

## Definition of Done (for any row to become ✅)
- Docs updated (this registry + relevant User Manual section)
- Uses **Local Control Plane** as SSOT (no parallel registries/stores)
- No regressions (children list/cards, add child flow, tabs, devtools unlock, alerts/reports pages)
- Async safety (router never renders promises; async after mount)
- Marker/guard/self-tests remain green (additive only)

## Feature inventory (epic-level)
> Detailed planning remains in:
> - `Docs/10_Parent_App/Implementation_Matrix.md`
> - `Docs/20_Child_App/Implementation_Matrix.md`

| Epic ID | Area | Epic | Status | Primary surfaces (Parent/Kid) | SSOT / Data contracts | Evidence (tests / notes) |
|---|---|---|---|---|---|---|
| EPIC-CORE-CHILDREN | Parent | Children: list/cards + child profile | ✅ | Parent: Dashboard → Children + Child Profile tabs | Control Plane: Children registry + policy read/write | “Never regress” checklist; self-test badge present |
| EPIC-CORE-DEVTOOLS | Parent | DevTools unlock + toggles | ✅ | Parent: hidden unlock gesture → DevTools | SSOT: dev flags/config | Marker/guard tests green |
| EPIC-CORE-REQUESTS | Parent↔Kid | Requests loop (kid request → parent approve/deny → kid applies grant) | ✅ | Parent: Requests UI; Kid: grant apply | SSOT: requests + grants | End-to-end loop verified previously (per tracker) |
| EPIC-POLICY-SCREENTIME | Policy | Screen time grace + warnings config | ✅ | Parent: policies UI | SSOT: policy model versioned | Implemented in 16R stream |
| EPIC-POLICY-APP-LIMITS | Policy | Per-app limits authoring | ✅ | Parent: per-app limits UI | SSOT: policy app limits | Implemented in 16S stream |
| EPIC-POLICY-WEB-SAFETY | Policy | SafeSearch/Restricted toggles persisted | ✅ | Parent: web safety toggles | SSOT: policy web safety | Implemented in 16T stream |
| EPIC-LOCATION-GEOFENCE | Location | Geofence authoring + overlay + evaluation + alerts | ✅ | Parent: geofence overlay UX; Kid: eval; Alerts: activity | SSOT: geofence config + event stream | Implemented 16U13–16U15 |
| EPIC-ALERTS-INBOX | Alerts | Alerts inbox: routing + grouping + ack | ✅ | Parent: Alerts inbox UI + ack | SSOT: alert routing/config + ack endpoints | Self-test PASS restored in 16W7a |
| EPIC-REPORTS | Reports | Reports scheduling (local digest) | 🟢 | Parent: reports scheduling surface + run-now | SSOT: policy.reports + reportsState + activity report_digest | 16W9: local scheduler + endpoints |
| EPIC-PAIRING | Kid/Devices | Pairing & provisioning (QR/code/deeplink) | 🟡 | Parent: pairing UX + device registry; Kid: pairing endpoint stub | SSOT: device registry + enrollment tokens | 26W08: token metadata persisted (issued/expires/revoked) + TTL enforced + revoke preserves record; **per-device lastSeen tracking + multi-device contract tests** |
| EPIC-POLICY-SYNC-RUNTIME | Kid Agent | Versioned sync + watchdog + rollback/integrity | 🟡 | Kid agent | Server now surfaces pending/overdue + apply errors via heartbeat status; integrity/self-repair still planned | KFT-006+ |
| EPIC-ENFORCE-SCREENTIME | Kid Agent | Enforcement: budgets/schedules/modes | 🟡 | Kid agent + block screens | SSOT: policy enforcement state | Config exists; enforcement partial |
| EPIC-ENFORCE-APPS | Kid Agent | Enforcement: app allow/deny + per-app limits + install approvals | 🟡 | Kid agent | SSOT: app rules + grants | Authoring exists; enforcement TBD |
| EPIC-ENFORCE-WEB | Kid Agent | Enforcement: categories + adult toggle + circumvention detection | 🟡 | Kid agent + alerts | SSOT: web rules + events | Toggles exist; enforcement TBD |
| EPIC-CHILD-UX | Kid UX | Child-facing “Today” + block screens + emergency access | 🟡 | Kid UX | `/today` shows pairing + policy version/updated + screen-time + next schedule; `/blocked` explains why + links to request; emergency access still stub | Evidence: `src/Safe0ne.ChildAgent/ChildUx/ChildUxServer.cs`, `src/Safe0ne.ChildAgent/ChildUx/ChildStateStore.cs` |
| EPIC-ACTIVITY | Telemetry | Activity capture + retention + export | 🔴 | Kid agent + Parent reports | SSOT: activity logs + retention policy | Planned in KFT-034+ |
| EPIC-ANTITAMPER | Resilience | Anti-tamper + fail-closed + self-repair | 🟡 | Parent: tamper/circumvention surfaces; Alerts inbox items | SSOT: heartbeat tamper/circumvention signals + activity | 16W15–16W17: signals surfaced + alerts + policy gates |
| EPIC-AUDIT | Parent | Audit log viewer (append-only policy changes) | 🟢 | Admin→Audit Log loads from SSOT; policy PUT/PATCH and device token revoke append entries | SSOT: append-only audit | 26W08: implemented SSOT-backed audit stream + endpoint; expand filters/export later |

## How to update this registry (every patch)
1. Identify affected epic(s) and specific features.
2. Update **Status** + **Evidence**.
3. Update the matching **User Manual** section under `Docs/90_User_Manual/`.
4. Ensure marker/self-tests remain green.

- 🟡 ENG: ControlPlane partial split — extracted Policy domain to JsonFileControlPlane.Policy.cs (seed)
