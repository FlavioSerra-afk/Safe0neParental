# Milestones & Patch Plan — Parent App (Controller) — Windows First (DETAILED)

Updated: 2026-02-02  
Status: Local Mode foundation implemented; Phase A = stub completeness priority

## Non‑negotiable delivery workflow (applies to every patch)
- Every patch is delivered as a **ZIP patch** containing all changed/new files in correct folder paths.
- Every patch ZIP includes `PATCH_NOTES.md` with:
  - Summary (what changed)
  - File list (added/changed with full paths)
  - How to apply (copy/replace exact paths)
  - Post-apply checks (run/build steps)
- One patch at a time. No “manual edits” unless explicitly unavoidable.
- Additive-only contract changes once coding begins.

## Ordering principle
- Keep sidebar compact; **all child policies live inside Child Profile**.


## Reality rebase (what exists in the repo today)
As of **2026-02-11**, the repo is **not** a fresh stub. The Windows-first Local Mode loop is already implemented and tests are green:

Implemented (do not regress):
- Local SSOT: JSON-backed **Local Control Plane** (`control-plane.v1.json`) in `Safe0ne.DashboardServer`
- Local API (Parent + Kid): children CRUD + archive/restore, profile get/put, devices pair/enroll/unpair, requests list/create/decision, activity get/post (batch), location get/post, diagnostics endpoints
- Parent UI: modular, local-first, async-safe hydration (no Promise rendering) under `wwwroot/app/features/*`
- DevTools: hidden unlock gesture + toggles + diagnostics viewers
- Router/self-test badge + marker/guard tests (anti-regression)

### Immediate priority: Phase A — "Stub Completeness First"
Before deeper enforcement/analytics, we complete the **full planned policy surface** end-to-end as *stubs* (UI + SSOT schema + Local API + Kid awareness/logging).

Phase A patch sequence (canonical numbering for this repo):
- **PATCH 13A** — Docs reality rebase (docs-only)
- **PATCH 13B** — SSOT policy schema expansion (additive defaults + auto-migration)
- **PATCH 13C** — Parent UI: Policy Editor stubs (full surface; stored in SSOT)
- **PATCH 13D** — Local API: policy version + effective policy endpoints
- **PATCH 13E** — Kid: policy polling + observable "would enforce" activity events
- **PATCH 13F** — Parent UI: Reports + Alerts stubs driven by Activity

(Older patch IDs P0–P11 remain useful as conceptual groupings, but the repo has progressed beyond "nothing implemented yet".)

- Build parent value loop in order: **Pair Devices → Set Policies → Handle Requests → View Reports → Respond to Alerts**.
- Data can be stubbed early, but UX flows and copy must match SSOT from day one.

## Platform notes (planning)
- Decide WebView2 runtime distribution early: **Evergreen vs Fixed Version**.
  - Evergreen vs fixed overview: https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/evergreen-vs-fixed-version
  - Distribution guidance: https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution
- “Push” is later; design for **policy version sync** where push is only a wake hint and pull-by-version is truth.
  - FCM architecture overview: https://firebase.google.com/docs/cloud-messaging/fcm-architecture

---

## PATCH P0 — Foundation: WebView2 shell + navigation + runtime readiness
### Goal
Create the Windows WebView2 parent console shell, compact sidebar, routing, and runtime readiness checks.

### Deliverables
- App frame: window chrome, sidebar, routing, base styling tokens
- Routes/pages exist (stubs ok):
  - Dashboard
  - Parent Profile
  - Children (list)
  - Alerts & Reports
  - Support
  - Advanced (optional)
- Empty state patterns: loading, no data, error
- WebView2 runtime readiness UX:
  - Detect missing runtime, show a friendly install/repair screen (depending on Evergreen/Fixed decision)

### Acceptance tests
- [ ] App launches; navigation works; no console errors
- [ ] Every page shows a plain-language placeholder (“What you can do here”)

---

## PATCH P1 — App foundation: state model + control-plane client stubs (UI only)
### Goal
Add typed UI state and a thin client layer that will talk to the Control Plane (local service now, cloud later).

### Deliverables
- UI state model for: Family, Child Profiles, Devices, Policies, Requests, Alerts, Reports
- Client interfaces (stubs/mocks allowed) for:
  - Pairing challenges
  - Policy upsert/get
  - Requests list/decide
  - Reports queries
  - Diagnostics export trigger
- Error handling patterns:
  - offline banner
  - retry affordances
  - non-blocking toasts

### Acceptance tests
- [ ] Switching between pages retains state appropriately
- [ ] All API calls can be served by deterministic mock data

---

## PATCH P2 — Dashboard MVP (metrics + one‑tap actions)
### Goal
Deliver a parent-friendly dashboard that answers: “Are my kids OK? What needs attention? What can I do fast?”

### Deliverables
- Metrics cards (Today + 7 days) per child:
  - Total screen time
  - Top apps / top sites
  - Blocked attempts count
  - Requests pending
  - Alerts summary
- One‑tap actions:
  - Lockdown / Open / Homework / Bedtime (global and/or per child)
  - Family Pause (temporary Lockdown for selected children)
- “Explain this” tooltips for every metric

### Acceptance tests
- [ ] Dashboard renders with sample data and empty states
- [ ] One‑tap actions require confirmation to avoid accidental lockouts

---

## PATCH P3 — Parent Profile (account, privacy, notifications, co‑parent)
### Goal
All parent-level settings in one place.

### Deliverables
- Account & security (UI-first; enforcement later):
  - Parent login state and session display
  - Co-parent roles/permissions (basic scaffolding)
- Privacy & data:
  - Data retention options (UI + explanation)
  - Export my data (UI; calls client stub)
- Notifications:
  - Alert categories toggles (requests, tamper, blocked attempts, risk alerts)
  - Frequency controls + quiet hours (UI)
- Subscription/billing placeholders (optional; disabled until launch)

### Acceptance tests
- [ ] Every setting has plain-language explanation + safe defaults
- [ ] No jargon in labels, descriptions, errors

---

## PATCH P4 — Children: CRUD + Child Profile scaffold (tabs + overview)
### Goal
Parents can add children and open a structured child profile.

### Deliverables
- Children list:
  - Add/Edit/Archive child
  - Age preset (affects recommended defaults)
- Child profile tabs exist and work:
  - Overview
  - Devices
  - Policies
  - Requests
  - Activity/Reports
  - Location (mobile later)

### Acceptance tests
- [ ] Parent can add a child; child profile opens correctly
- [ ] Tabs never dead-end; always show a useful empty state

---

## PATCH P5 — Child Profile > Devices (pairing wizard + device health)
### Goal
Parents can pair devices to each child and see device status.

### Deliverables
- Pair device wizard:
  - Choose device type (Windows/Mac/Android/iOS)
  - Generate pairing code + QR
  - Expiry countdown + regenerate action
- Device list per child:
  - Rename device
  - Unpair device (confirm)
  - Health indicators (last seen, version applied, warnings)

### Acceptance tests
- [ ] Pairing UI is non-technical and unambiguous
- [ ] Unpair requires multi-step confirmation

---

## PATCH P6 — Child Profile > Policies v1: Modes + Always Allowed + Screen Time + Schedules
### Goal
Deliver the most important policy controls first.

### Deliverables
- One‑tap Modes: Lockdown/Open/Homework/Bedtime with plain-language explanations
- Always Allowed editor:
  - emergency contacts
  - school essentials
  - parent-approved safe apps/sites
- Screen time:
  - daily budget
  - bedtime schedule
  - school time schedule
  - homework schedule
- “Policy precedence” explainer panel:
  - Always Allowed > Grants > Mode > Schedules

### Acceptance tests
- [ ] Parent can save policies per child without confusion
- [ ] Each setting explains what happens when turned on

---

## PATCH P7 — Policies v2: Apps & games controls
### Goal
App-level controls and install approvals.

### Deliverables
- App allow/deny list UI + presets
- Per‑app time limits UI
- Install approval workflow UI (platform caveats shown clearly)
- “New apps require review” option (if planned)

### Acceptance tests
- [ ] Platform caveats are visible (especially iOS constraints)
- [ ] Defaults are safe but not overly restrictive

---

## PATCH P8 — Policies v3: Web & content filtering
### Goal
Web categories, adult content blocking, and exceptions.

### Deliverables
- Category filtering: Allow / Alert / Block
- Explicit Adult/Porn toggle
- Allowed domains + blocked domains lists
- Circumvention toggles (VPN/proxy/private DNS detection) with “best effort” notes

### Acceptance tests
- [ ] Parents can see what’s blocked and why
- [ ] Exceptions are easy to add and review

---

## PATCH P9 — Policies v4: Social & communication safety + AI alerts config (opt‑in)
### Goal
Social controls and alert-based safety signals (opt-in).

### Deliverables
- Social app time limits UI
- Communication rules UI:
  - calls/SMS (Android-focused; show “not supported” on platforms where not feasible)
- Safety signals configuration:
  - bullying/grooming/self-harm risk alerts (opt-in)
  - explainability and privacy notes

### Acceptance tests
- [ ] Opt-in explains what data is used and what parents receive
- [ ] Parents can disable at any time

---

## PATCH P10 — Policies v5: Location & safety (mobile-forward)
### Goal
Location policies and views (enforcement arrives later with mobile agents).

### Deliverables
- Location sharing toggle (per child/device)
- Geofences (Home/School) add/edit UI
- SOS & check-in configuration UI

### Acceptance tests
- [ ] UI is ready for Android/iOS later without redesign

---

## PATCH P11 — Requests & approvals (parent decision loop)
### Goal
Parents can see and decide child requests.

### Deliverables
- Requests inbox:
  - per child
  - global “All requests”
- Decisions:
  - Approve/Deny
  - Conditions (duration, time window, only specific app/site)
- Audit trail:
  - who approved
  - when
  - what conditions

### Acceptance tests
- [ ] Decision is clearly logged and reviewable
- [ ] Parent sees effect (grant) reflected immediately

---

## PATCH P12 — Alerts & reports (family-wide)
### Goal
Reports parents can understand fast + alerts they can act on.

### Deliverables
- Alerts inbox:
  - filter by child/device/severity/type
  - acknowledge/resolve
- Reports:
  - weekly rollups
  - timeline view
  - “top changes” (new blocked attempts, new apps, policy changes)
- Report scheduling UI (email/push later)

### Acceptance tests
- [ ] Parents can answer “what changed?” quickly
- [ ] Alerts support action (open child, open policy, open requests)

---

## PATCH P13 — Support & recovery (safe resets + diagnostics)
### Goal
Support pages that reduce support burden and prevent dangerous actions.

### Deliverables
- Help center pages + troubleshooting wizard
- Diagnostics export trigger UI
- Reset/wipe flows:
  - multi-step confirmation
  - consequences explained clearly

### Acceptance tests
- [ ] No accidental wipes possible
- [ ] Support text is plain-language

---

## PATCH P14 — Polish pass (copy, tooltips, accessibility, empty states)
### Goal
Final UX pass: clarity, consistency, accessibility, trust.

### Deliverables
- Tooltip pass on every setting
- Copy rewrite pass (no jargon)
- Accessibility checks (keyboard nav, contrast, focus states)
- Improved empty/error states everywhere

### Acceptance tests
- [ ] Every setting explains itself without needing external docs
