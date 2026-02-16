# Appendix — Patch ↔ Implementation Matrix Mapping (Parent + Child)

Updated: 2026-02-02  
Purpose: Keep implementation direction consistent and make progress measurable.

## How to maintain a stable mapping
1) In each app’s `Implementation_Matrix.md`, add a leftmost column: **Feature ID**.
2) Assign IDs in **top-to-bottom order** from the Master Features tree:
   - Parent features: `PFT-001`, `PFT-002`, …
   - Child features: `KFT-001`, `KFT-002`, …
3) Once IDs exist, never renumber them. If you add new features later:
   - append them at the end or insert with suffixes (e.g., `PFT-045a`) to avoid renumbering.
4) Each patch MUST list exactly which Feature IDs it “checks off”.
5) When a patch ships for multiple platforms later, tick per-platform checkboxes in the matrix row.

## Patch → Matrix mapping (section-based until IDs are assigned)

### Parent App patches
- **P0** ticks: PFT-008 (Windows shell + navigation scaffold)
- **P1** ticks: PFT-005, PFT-006, PFT-007 (contracts + API stubs + versioned policy model)
- **P2** checks off: Dashboard metrics + one-tap actions
- **P3** checks off: Parent Profile settings (privacy, notifications, exports, roles)
- **P4** checks off: Children CRUD + Child Profile scaffold (tabs)
- **P5** checks off: Devices tab (pairing UI + device list + health)
- **P6** checks off: Policies (Modes + Always Allowed + Screen Time + Schedules)
- **P7** checks off: Policies (Apps & games)
- **P8** checks off: Policies (Web & content filtering)
- **P9** checks off: Policies (Social/communication safety + AI alerts config opt-in)
- **P10** checks off: Policies (Location & safety UI)
- **P11** checks off: Requests inbox + approvals + audit
- **P12** checks off: Alerts inbox + reports (weekly rollups/timeline)
- **P13** checks off: Support + diagnostics + reset/wipe UX
- **P14** checks off: Polish pass (tooltips, copy, accessibility, empty-states)

### Child App patches
- **K0** checks off: Agent foundation + identity + diagnostics
- **K1** checks off: Pairing claim + permissions onboarding
- **K2** checks off: Policy sync + cache + rollback
- **K3** checks off: Precedence + modes engine
- **K4** checks off: Screen time enforcement
- **K5** checks off: App/game controls
- **K6** checks off: Web/content filtering + circumvention best-effort
- **K7** checks off: Child UX (Today + block screens + emergency access surface)
- **K8** checks off: Requests create/send + offline queue behavior
- **K9** checks off: Telemetry aggregates + heartbeat + diagnostics bundle
- **K10** checks off: Anti-tamper + fail-closed behavior
- **K11** checks off: Mobile-only interfaces stubs (location/geofence/SOS etc.)
- **K12** checks off: Polish + performance hardening

## Convert to “exact Feature IDs” (required before coding starts)
Before implementation begins:
- Update each patch section to include an explicit list like:
  - `Ticks: PFT-001, PFT-002, PFT-014…`
  - `Ticks: KFT-003, KFT-004…`
This prevents roadmap drift during patch iterations.
