# Safe0ne Parental — Parent App — Implementation Matrix

Updated: 2026-02-16

Legend:
- ✅ Implemented (wired to SSOT / Local Control Plane, visible in UI, basic tests/markers where applicable)
- 🟡 Partial (UI or stubs exist; incomplete wiring or enforcement/analytics)
- 🔴 Not implemented
- — Not applicable

> **Reality Rebase (additive):** This matrix reflects *current repo reality* as of shipped patch stream **16R → 16W7a** while preserving the original planned feature set. Planned items remain listed even if not yet implemented.

## Shipped patches (append-only)
- PATCH_16U13_geofence_fullwidth_overlay_ux
- PATCH_16U13a_geofence_overlay_zindex_fix
- PATCH_16U13b_geofence_overlay_fit_icons_tooltips
- PATCH_16U13c_geofence_overlay_right_transparent
- PATCH_16U13d_geofence_overlay_compact_taller_map
- PATCH_16U14_geofence_eval_kid_agent
- PATCH_16U15_geofence_alerts_activity
- PATCH_16R_screen_time_grace_warnings_config
- PATCH_16S_per_app_limits_authoring_ui_validation
- PATCH_16T_safesearch_restricted_toggles_persisted
- PATCH_16V_alerts_routing_config_persisted
- PATCH_16W1_alerts_inbox_routing_ui_polish
- PATCH_16W2_alerts_inbox_grouping_filters
- PATCH_16W3_alerts_ack_ssot_compact
- PATCH_16W4_alerts_ack_ssot_endpoints
- PATCH_16W5_alerts_inbox_compact_toolbar_fix
- PATCH_16W6_alerts_inbox_compact_grouped
- PATCH_16W7_reports_scheduling_stub
- PATCH_16W7a_alerts_js_syntax_fix

## Compatibility assumptions
Parent App is **Windows-first** (WPF + WebView2). Cross-platform flags below are planning markers only.

- **PC (Windows)**: primary target ✅
- **Mac / Android / iOS / Web**: planning only 🟡/🔴

## Core areas (Parent App)

| Feature ID | Area | Feature | Compatible | Status (PC) | Notes / Patch linkage |
|---|---|---|---|---|---|
| P-FOUND-001 | Foundation | Shared contracts (DTOs/enums) for Parent + Local Service | Win✅ | ✅ | Present in repo; baseline foundation. |
| P-FOUND-002 | Foundation | Local Control Plane API stubs (children list + policy read/write) | Win✅ | ✅ | SSOT/local-first established. |
| P-FOUND-003 | Foundation | Versioned policy model (`PolicyVersion` monotonic) | Win✅ | ✅ | Mentioned in shared SSOT doc. |
| P-UI-001 | Shell | WPF + WebView2 host + navigation scaffold | Win✅ | ✅ | Parent App shell operational. |
| P-CHILD-001 | Children | Children list/cards | Win✅ | ✅ | **Never regress**. |
| P-CHILD-002 | Children | Add Child modal flow | Win✅ | ✅ | **Never regress**. |
| P-CHILD-003 | Children | Avatar crop tools | Win✅ | ✅ | **Never regress**. |
| P-CHILD-004 | Child Profile | Profile tabs (Policies / Alerts / Requests / Reports etc.) | Win✅ | ✅ | **Never regress**. |
| P-DEV-001 | DevTools | Hidden unlock gesture + toggles | Win✅ | ✅ | **Never regress**. |
| P-ROUTER-001 | DashboardServer UI | Thin router + modular features | Win✅ | ✅ | ADR-0003 / module policy is enforced in repo structure. |
| P-REQ-001 | Requests | Request loop UI (approve/deny) | Win✅ | ✅ | End-to-end loop is a must-have baseline. |
| P-REQ-002 | Requests | Grant issuance (time-boxed exceptions) stored in SSOT | Win✅ | ✅ | Stored in Local Control Plane; kid applies grants. |
| P-ALERT-001 | Alerts | Alerts routing config persisted | Win✅ | ✅ | PATCH_16V |
| P-ALERT-002 | Alerts | Alerts inbox: routing UI polish | Win✅ | ✅ | PATCH_16W1 |
| P-ALERT-003 | Alerts | Alerts inbox: grouping + filters | Win✅ | ✅ | PATCH_16W2 |
| P-ALERT-004 | Alerts | Alerts acknowledgment stored in SSOT (compact model) | Win✅ | ✅ | PATCH_16W3 |
| P-ALERT-005 | Alerts | Alerts ack endpoints | Win✅ | ✅ | PATCH_16W4 |
| P-ALERT-006 | Alerts | Inbox toolbar + compact layout fixes | Win✅ | ✅ | PATCH_16W5, 16W6 |
| P-REPORT-001 | Reports | Reports scheduling + local runner | Win✅ | ✅ | Local runner emits `report_digest` events; surfaced in Reports UI. |
| P-POL-001 | Policies | Screen time grace + warnings config | Win✅ | ✅ | PATCH_16R |
| P-POL-002 | Policies | Per-app limits authoring UI validation | Win✅ | ✅ | PATCH_16S |
| P-POL-003 | Policies | SafeSearch / Restricted toggles persisted | Win✅ | ✅ | PATCH_16T |
| P-LOC-001 | Location | Geofence overlay UX in child profile | Win✅ | ✅ | PATCH_16U13 → 16U13d |
| P-LOC-002 | Location | Geofence transitions emitted as Alerts/Activity | Win✅ | ✅ | PATCH_16U15 |
| P-DEVPAIR-001 | Devices | Pair/assign devices to child | Win✅ | 🟡 | Devices tab + pairing code flow implemented (local-first). |
| P-HEALTH-001 | Devices | Device health (per-device heartbeat/last seen) | Win✅ | ✅ | Devices tab shows Online/Offline + per-device last-seen from authenticated heartbeats. |
| P-ANTITAMPER-001 | Security | Anti-tamper resilience (stubs) | Win✅ | 🟡 | Planned; not shipped. |
| P-AUDIT-001 | Compliance | Append-only audit log for policy changes | Win✅ | ✅ | Admin → Audit Log viewer; SSOT-backed audit stream per child. |

## Cross-platform status (planning markers)

| Platform | Parent App UI | Local Control Plane | Notes |
|---|---:|---:|---|
| Windows | ✅ | ✅ | Current focus; shipping patches. |
| macOS | 🟡 | 🟡 | Future; depends on host shell choice. |
| Android | 🔴 | 🔴 | Future; policy model remains compatible. |
| iOS | 🔴 | 🔴 | Future; policy model remains compatible. |
| Web | 🟡 | 🟡 | Could be a thin UI over local/remote control plane later. |
