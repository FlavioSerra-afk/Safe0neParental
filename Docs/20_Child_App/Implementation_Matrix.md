# Safe0ne Parental — Child App (Agent + Kid UX) — Implementation Matrix

Updated: 2026-02-16

Legend:
- ✅ Implemented (wired to SSOT / Local Control Plane, observable behavior, basic markers/tests where applicable)
- 🟡 Partial (stubs/UI exist; incomplete wiring or enforcement)
- 🔴 Not implemented
- — Not applicable

> **Reality Rebase (additive):** This matrix reflects repo reality as of shipped patch stream **16R → 16W7a** while preserving all planned child-side features.

## Shipped patches impacting Child App
- PATCH_16U14_geofence_eval_kid_agent (agent evaluation wiring)
- PATCH_16R_screen_time_grace_warnings_config (policy surface; enforcement may be partial)
- PATCH_16S_per_app_limits_authoring_ui_validation (policy surface; enforcement may be partial)
- PATCH_16T_safesearch_restricted_toggles_persisted (policy persistence; enforcement may be partial)

## Child components
- **Kid Agent (Windows-first):** enforcement + telemetry + applying grants/policies
- **Kid UX:** child-facing UI (requests, status, “why”, etc.)

## Implementation matrix

| Feature ID | Component | Feature | Compatible | Status (Windows) | Notes / Patch linkage |
|---|---|---|---|---|---|
| K-FOUND-001 | Foundation | Consume shared contracts (DTOs/enums) | Win✅ | ✅ | Shared contracts baseline. |
| K-SSOT-001 | SSOT | Read policy + grants from Local Control Plane | Win✅ | ✅ | Required for request loop. |
| K-REQ-001 | Agent | Apply approved grants (time-boxed exceptions) | Win✅ | ✅ | “Request loop” baseline. |
| K-REQ-002 | Kid UX | Create/request exceptions (child → parent) | Win✅ | ✅ | Must remain working. |
| K-LOC-001 | Agent | Geofence evaluation (inside/outside) | Win✅ | ✅ | PATCH_16U14 |
| K-LOC-002 | Agent | Emit geofence enter/exit events | Win✅ | ✅ | Activity events consumed by Alerts (PATCH_16U15) |
| K-TIME-001 | Agent | Time budget enforcement (daily minutes) | Win✅ | 🟡 | Surface exists; enforcement may be staged. |
| K-TIME-002 | Agent | Grace minutes + warnings | Win✅ | 🟡 | PATCH_16R adds config; enforcement may be partial. |
| K-APP-001 | Agent | App allow/deny list | Win✅ | 🟡 | Planned; not fully enforced. |
| K-APP-002 | Agent | Per-app limits | Win✅ | 🟡 | PATCH_16S improves authoring; enforcement TBD. |
| K-WEB-001 | Agent | SafeSearch / Restricted mode enforcement | Win✅ | 🟡 | PATCH_16T persists toggles; enforcement TBD. |
| K-ALERT-001 | Agent | Telemetry → Alerts/Activity pipeline | Win✅ | ✅ | Geofence activity verified; other signals planned. |
| K-UX-001 | Kid UX | “Why am I blocked?” explanations + request path | Win✅ | 🟡 | Planned UX baseline; ensure no-jargon. |
| K-HEALTH-001 | Agent | Heartbeat / health reporting | Win✅ | 🟡 | Planned; part of pairing/health hardening. |
| K-ANTITAMPER-001 | Agent | Anti-tamper stubs | Win✅ | 🟡 | Signals reported in heartbeat; parent alerting + policy gates implemented (16W15–16W17). Enforcement hardening still TBD. |

## Notes
- WebView2 “Tracking Prevention blocked storage” warnings are expected; **localStorage is best-effort only**.
- Child-side must remain compatible with SSOT precedence order (Always Allowed → Grants → Mode → Schedules/Budgets).
