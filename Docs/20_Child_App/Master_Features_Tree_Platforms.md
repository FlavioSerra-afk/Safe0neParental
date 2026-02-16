# Child App (Kids/Agent) — Master Features (Tree + Platform Compatibility)

*Fresh build tracker (nothing implemented yet). Updated: 2026-02-02.*

## Platform legend
- **Win** = Windows child device agent (service + optional limited UI)
- **Mac** = macOS child device agent (daemon + optional limited UI)
- **Android** = Android Kids app
- **iOS** = iPhone/iPad Kids app *(note: platform constraints)*
- **Web** = N/A for kids app (kids app is device-side)

Values:
- ✅ target/feasible
- ⚠️ feasible but OS-limited / requires special setup
- — not applicable

---

## 1) Pairing & Provisioning
- Pair device to **family** + **child profile** (QR / pairing code / deep link) *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Permissions wizard (plain language; explains “why”) *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Offline-first provisioning (cache last policy; enforce even offline) *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Device identity (name, type, OS version) *(Win✅ Mac✅ Android✅ iOS✅ Web—)*

## 2) Policy Sync & Runtime
- Background agent (service/daemon) with watchdog *(Win✅ Mac✅ Android✅ iOS⚠️ Web—)*
- Policy sync (versioned), safe rollback, integrity checks *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Precedence rules engine:
  - Lockdown overrides everything
  - Always Allowed always wins
  - Time-boxed grants override blocks temporarily *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Telemetry heartbeat (last seen, policy applied) *(Win✅ Mac✅ Android✅ iOS✅ Web—)*

## 3) Enforcement Modules

### 3.1 Screen time & routines
- Daily screen-time budgets *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Schedules: Bedtime / School / Homework *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Modes: Lockdown / Open / Homework / Bedtime *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Grace periods + warnings (5 min / 1 min) *(Win✅ Mac✅ Android✅ iOS✅ Web—)*

### 3.2 Apps & games controls
- App allow/deny list *(Win✅ Mac✅ Android✅ iOS⚠️ Web—)*
- Per-app time limits *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Install controls / require approval (where supported) *(Win✅ Mac✅ Android✅ iOS⚠️ Web—)*
- Block “newly installed apps until approved” *(Win✅ Mac✅ Android✅ iOS⚠️ Web—)*

### 3.3 Web & content filtering
- Category-based filtering (allow/alert/block) *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Explicit “Adult/Porn blocking” toggle *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- SafeSearch / Restricted Mode enforcement (best effort) *(Win✅ Mac✅ Android✅ iOS⚠️ Web—)*
- Circumvention detection (VPN/proxy/private DNS) + alerts (best effort) *(Win✅ Mac✅ Android✅ iOS⚠️ Web—)*

### 3.4 Social & communication safety
- Social app time limits (supported apps) *(Win✅ Mac✅ Android✅ iOS⚠️ Web—)*
- Calls/SMS monitoring + blocked numbers *(Win— Mac— Android✅ iOS⚠️ Web—)*
- AI/heuristic risk signals (bullying/grooming/self-harm keywords) **opt-in** *(Win✅ Mac✅ Android✅ iOS⚠️ Web—)*

### 3.5 Location & safety (mobile-first)
- Live location + history *(Win— Mac— Android✅ iOS✅ Web—)*
- Geofencing (home/school) arrive/leave alerts *(Win— Mac— Android✅ iOS✅ Web—)*
- SOS button + optional location *(Win— Mac— Android✅ iOS✅ Web—)*
- “I’m OK” check-in *(Win— Mac— Android✅ iOS✅ Web—)*

## 4) Child-facing UX (small, clear, hard to bypass)
- “Today” screen: time left, next lock, current mode *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Block screens: what’s blocked + why + next steps *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Request access:
  - more time
  - unblock app
  - unblock site
  - include reason *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Emergency access (Always Allowed): parent contact, emergency numbers, school essentials *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Optional “Goals & rewards” view (earned minutes) *(Win✅ Mac✅ Android✅ iOS✅ Web—)*

## 5) Activity Capture (privacy-first)
- App usage totals + blocked attempts *(Win✅ Mac✅ Android✅ iOS⚠️ Web—)*
- Web domain/category totals + blocked attempts *(Win✅ Mac✅ Android✅ iOS⚠️ Web—)*
- Local log retention + rotation *(Win✅ Mac✅ Android✅ iOS✅ Web—)*
- Export diagnostic bundle (logs + health snapshot) *(Win✅ Mac✅ Android✅ iOS✅ Web—)*

## 6) Anti-tamper & resilience
- Uninstall protection (best effort; OS dependent) *(Win✅ Mac✅ Android✅ iOS⚠️ Web—)*
- Detect permissions removal / agent disabled + notify parent *(Win✅ Mac✅ Android✅ iOS⚠️ Web—)*
- “Fail-closed” safety mode: if agent broken, enter Restricted until parent fixes *(Win✅ Mac✅ Android✅ iOS⚠️ Web—)*
- Self-repair flows (re-pair, re-request permissions, restart agent) *(Win✅ Mac✅ Android✅ iOS✅ Web—)*

## 7) Optional AI-assisted experiences (keep costs optional)
- On-device heuristic suggestions (no cloud) *(Win✅ Mac✅ Android✅ iOS⚠️ Web—)*
- AI-assisted request suggestion (duration/conditions) **parent must approve** *(Win✅ Mac✅ Android✅ iOS✅ Web—)*

---

## References (why two-app model is standard)
- Kids/agent app runs on child device and enables monitoring/blocking; separate from parent controller app.
