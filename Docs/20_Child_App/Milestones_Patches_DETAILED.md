# Milestones & Patch Plan — Child App (Kids/Agent) — Windows First (DETAILED)

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
- Enforcement first: **Modes → Time → Apps → Web**


## Reality rebase (what exists in the repo today)
As of **2026-02-11**, the Windows-first Child Agent already has a functioning Local Mode loop:

Implemented (do not regress):
- Enroll via pairing code (`/api/local/devices/enroll`)
- Requests submit + decision polling (local-first)
- Activity outbox + batch flush to Local SSOT
- Location sender scaffold + `SAFEONE_FAKE_LOCATION`
- Policy polling/apply scaffold exists (currently supports a limited v1 surface)

### Immediate priority: Phase A — observable enforcement scaffolding
Phase A expands the policy surface (schema + UI) and makes the agent **observe** policy changes end-to-end via Activity:
- Poll policy version; when it changes, compute a deterministic effective mode (Lockdown precedence, etc.)
- Emit Activity events: `policy_applied` and `policy_would_enforce_*` (no OS-level blocking required for new surfaces yet)

The Kid-side work for Phase A lands in **PATCH 13E**.

- Then child UX: **Today + block screens**
- Then parent loop: **Requests**
- Then stability: **Telemetry + Anti-tamper**
- Design with iOS/Android constraints in mind:
  - iOS Screen Time frameworks: FamilyControls + ManagedSettings + DeviceActivity
    - Screen Time Technology Frameworks: https://developer.apple.com/documentation/screentimeapidocumentation/
    - Configuring Family Controls entitlement: https://developer.apple.com/documentation/xcode/configuring-family-controls
    - Screen Time framework (web usage): https://developer.apple.com/documentation/screentime
  - Android device policy surface:
    - DevicePolicyManager reference: https://developer.android.com/reference/android/app/admin/DevicePolicyManager

## Platform notes (planning)
- iOS child restrictions should align with Screen Time frameworks and entitlements; the system is designed to preserve privacy while enabling parental restrictions.
- Android enforcement depends on provisioning mode and admin role (device owner/profile owner), which shapes what policies can be enforced.

---

## PATCH K0 — Foundation: agent/service skeleton + device identity + diagnostics
### Goal
Create the Windows child agent foundation: service/daemon skeleton, device identity, logging, and minimal health/diagnostics surface.

### Deliverables
- Agent/service starts reliably (dev mode ok)
- Device identity (device name/type/OS)
- Logging + log retention/rotation plan (privacy-first)
- Diagnostics surface (health status, last policy version applied placeholder)

### Acceptance tests
- [ ] Agent runs without crashing for 30+ minutes
- [ ] Diagnostics show meaningful status

---

## PATCH K1 — Pairing claim + permissions onboarding (child-side)
### Goal
Implement pairing claim (enter code/deep link), permissions onboarding UX, and offline-first provisioning.

### Deliverables
- Pairing claim UI:
  - Enter pairing code
  - Confirm child name + device name
- Permissions wizard:
  - Plain language “why we need this”
  - “What happens if you deny”
- Offline-first provisioning behavior defined

### Acceptance tests
- [ ] Child can complete pairing using a code
- [ ] Child UI never exposes parent controls

---

## PATCH K2 — Policy sync: versioned fetch + cache + rollback
### Goal
Implement policy sync by version, cached last-known-good behavior, integrity checks, and rollback when apply fails.

### Deliverables
- Policy versioning model
- Local cache store
- Rollback rule:
  - If policy apply fails → revert to last-known-good and alert parent

### Acceptance tests
- [ ] Agent enforces cached policy offline
- [ ] Rollback is recorded and surfaced (later via parent alerts)

---

## PATCH K3 — Policy engine: precedence + modes (Lockdown/Open/Homework/Bedtime)
### Goal
Implement precedence rules and mode state machine so enforcement is deterministic.

### Deliverables
- Precedence (must match SSOT):
  1) Always Allowed
  2) Grants (time-boxed exceptions)
  3) Mode
  4) Scheduled rules
- Mode engine supports:
  - Lockdown
  - Open
  - Homework
  - Bedtime
- “Effective policy” computation output (debuggable)

### Acceptance tests
- [ ] Same inputs produce same effective policy
- [ ] Mode changes apply safely and immediately

---

## PATCH K4 — Screen time enforcement v1
### Goal
Enforce daily budgets and schedules (bedtime/school/homework), including warnings and safe lock transitions.

### Deliverables
- Daily time budget logic
- Schedule windows logic
- Warnings:
  - 5 min remaining
  - 1 min remaining
- Lock transition behavior defined (graceful; explainable)

### Acceptance tests
- [ ] Budget depletion triggers restriction with clear UX
- [ ] Schedules activate/deactivate correctly

---

## PATCH K5 — App & games controls v1
### Goal
Implement app allow/deny and per-app limits (best effort by OS), plus blocked-attempt telemetry.

### Deliverables
- App identification approach (Windows-first plan)
- Allow/deny enforcement behavior (best effort)
- Per-app time limit enforcement behavior
- Blocked attempts logged (privacy-first)

### Acceptance tests
- [ ] Blocked apps are restricted per chosen method
- [ ] Per-app limits count time correctly

---

## PATCH K6 — Web & content filtering v1
### Goal
Implement category filtering, explicit adult toggle, allow/block exceptions, and circumvention detection (best effort).

### Deliverables
- Category decision engine
- Domain allow/block exceptions
- Adult/Porn toggle
- Circumvention signals:
  - VPN/proxy/private DNS detection where possible
  - Alerts to parent when suspected

### Acceptance tests
- [ ] Blocked categories/domains show a block screen
- [ ] Exceptions work reliably

---

## PATCH K7 — Child UX: Today view + block screens (explainable)
### Goal
Minimal child-facing UX: time left, next lock, block screens with reasons, emergency access.

### Deliverables
- “Today” screen:
  - Time remaining
  - Next schedule change
  - Current mode
  - Pairing status + policy version/updated + last evaluation timestamp (local visibility)
- Block screens:
  - What’s blocked
  - Why it’s blocked
  - What the child can do next (request access)
- Emergency access list (Always Allowed surfaced clearly)

### Acceptance tests
- [ ] Child always knows what’s happening and next steps
- [ ] No technical language on child screens

---

## PATCH K8 — Requests: create & send reliably (child → parent)
### Goal
Implement request creation with reasons and reliable delivery.

### Deliverables
- Request types:
  - More time
  - Unblock app
  - Unblock site
- Request reason (optional)
- Retry strategy when offline (queue + send later)

### Acceptance tests
- [ ] Requests never silently fail (queued status shown)
- [ ] Duplicate prevention (avoid spam)

---

## PATCH K9 — Telemetry: activity aggregates + heartbeat + diagnostics bundle
### Goal
Privacy-first metrics pipeline for parent dashboard and troubleshooting.

### Deliverables
- Heartbeat:
  - Last seen
  - Policy version applied
  - Module health (time/app/web)
- Activity aggregates:
  - App usage totals
  - Web category totals
  - Blocked attempts counts
- Log retention and export bundle

### Acceptance tests
- [ ] Parents can trust “device is working” indicators
- [ ] Logs are minimal by default

---

## PATCH K10 — Anti-tamper + resilience
### Goal
Detect tamper signals, protect/alert on disable/uninstall (best effort), fail-closed restricted mode.

### Deliverables
- Detect:
  - Agent stopped
  - Permissions removed
  - Policy apply failures
- Fail-closed:
  - If enforcement broken → restricted mode until parent resolves
- Alerts emitted to parent

### Acceptance tests
- [ ] Tamper attempt produces a clear alert
- [ ] Emergency access remains available

---

## PATCH K11 — Platform stubs for Android/iOS/mobile-only features (planning hooks)
### Goal
Add interface stubs and planning hooks for mobile-only features.

### Deliverables
- Location/geofencing/SOS interfaces (Android/iOS)
- Calls/SMS interfaces (Android)
- iOS Screen Time integration plan anchored to the Screen Time framework suite

### Acceptance tests
- [ ] Mobile rollout later won’t require data-model redesign

---

## PATCH K12 — Polish + performance hardening
### Goal
Reduce false positives, improve stability/performance, align child copy with parent explanations.

### Deliverables
- Copy alignment pass
- Performance profiling plan
- Stability + recovery flows (self-repair prompts)

### Acceptance tests
- [ ] Child UX is calm, clear, consistent
