# Appendix — Patch ↔ Feature Matrix Mapping (Legacy → 16* Stream)

Updated: 2026-02-16

Purpose:
- Provide a **single mapping table** that relates historic/legacy milestone labels (P*/K*/13*) to the newer **16* patch stream**.
- Keep this document **append-only** (do not delete older planning labels).

## Reality Rebase statement (additive)
Legacy labels remain useful for planning and coverage, but **implementation tracking is authoritative in the 16* patch stream** (because patches correspond to concrete ZIP-delivered changes).

## Mapping table

| Legacy label (example) | Intended area | 16* patch stream mapping | Notes |
|---|---|---|---|
| 13U13 (Geofence overlay UX) | Parent Child Profile (Location) | 16U13 → 16U13d | UX iteration series (overlay layout/z-index/fit/compact). |
| 13U14 (Geofence eval) | Kid Agent | 16U14 | Geofence inside/outside evaluation wiring. |
| 13U15 (Geofence alerts) | Alerts/Activity | 16U15 | Activity-driven alert events for geofence transitions. |
| P-Policies-TimeBudget-001 | Parent policy authoring | 16R | Grace minutes + warnings config surface. |
| P-Policies-Apps-PerApp-001 | Parent policy authoring | 16S | Per-app limits authoring UI validation. |
| P-Policies-Web-SafeSearch-001 | Parent policy authoring | 16T | SafeSearch/Restricted toggles persisted. |
| P-Alerts-Routing-001 | Alerts plumbing | 16V | Alerts routing config persisted. |
| P-Alerts-Inbox-001 | Alerts inbox UI | 16W1 → 16W6 | Inbox polish, grouping/filters, compact toolbar, etc. |
| P-Reports-Scheduling-001 | Reports | 16W7 | Reports scheduling **stub**. |
| P-Alerts-Hotfix-Parse-001 | Alerts JS | 16W7a | Fix alerts.js syntax error; restore self-test PASS. |

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

## How to extend this mapping
1. When a legacy doc references `P*`, `K*`, or `13*` IDs, add a row above mapping it to one or more **16*** patches.
2. If no patch exists yet, use `TBD (backlog)` and include the intended next patch ID (e.g., `16W8`).
