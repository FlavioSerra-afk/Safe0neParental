# Parental Control App — Master Features (Tree + Platform Compatibility)

*Planning tracker for a fresh rebuild (nothing implemented yet). Updated: 2026-02-02.*

## Platform key
- **PC (Win)** = Windows desktop/laptop agent + parent console
- **Mac** = macOS agent
- **Android** = Android child device agent
- **iOS** = iPhone/iPad child device management (note: platform constraints)
- **Web** = cloud web portal (optional)

Legend for platform values:
- ✅ = feasible / target
- ⚠️ = possible but limited by OS policies / needs special setup
- — = not applicable

---

## Sidebar (compact)
- **Dashboard**
- **Parent Profile**
- **Children** (each child contains **Devices + Policies + Activity**)
- **Family Alerts & Reports**
- **Support & Safety**
- **Admin / Advanced** (optional)

> Design decision: **All policy rules live inside each Child Profile**, not as a top-level “Rules” area.
> This keeps the sidebar small and matches how parents think: “Pick a child → manage their devices + rules.”  

---

# 1) Dashboard (metrics + quick actions)

- **Overview metrics**
  - Daily/weekly screen time, top apps/sites, blocked attempts, alerts count  
    *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- **Quick actions**
  - **Family Pause / Pause internet** (pause per child or whole family) citeturn1view0  
    *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
  - **Modes**: Lockdown (Block everything), Open (Unblock), Homework, Bedtime  
    *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- **Highlights**
  - “What changed since yesterday” (new apps installed, new risky categories, tamper events)  
    *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- **AI Summary (optional)**
  - Daily digest + recommended actions (never auto-enforce without parent approval) citeturn1view0turn0search1  
    *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

---

# 2) Parent Profile (all parent settings live here)

## 2.1 Parent account & security
- Parent accounts, roles (co‑parent / guardian) “Additional parent” citeturn1view0  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- Login security: PIN/biometrics (mobile), 2FA (web), device trust  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

## 2.2 Family & household settings
- Family members, child list, household timezone, “school schedule” templates  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

## 2.3 Notifications
- Push/email/SMS alerts; alert severity controls  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

## 2.4 Privacy, data & exports
- Data retention controls (privacy-first by default)
- Export reports (PDF/CSV/JSON), incident timeline export  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

## 2.5 Subscription / billing (optional)
- Plans, device limits, receipts  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

---

# 3) Children (core area)

## 3.1 Child List
- Add/edit/remove child
- Age presets (6–9 / 10–12 / 13–15 / 16+) + explain “why”  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

## 3.2 Child Profile (tree)
Each child profile contains:

### A) Overview
- At‑a‑glance: today’s time, key alerts, most used apps, next scheduled lock  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

### B) Devices (pair + assign + health)
- Pair device to child; rename; assign “primary device”
- Device health: last seen, policy sync, OS version, tamper status  
  *(PC✅ Mac✅ Android✅ iOS⚠️ Web✅)*

Supported device types:
- Windows PC *(✅)*
- macOS *(✅)*
- Android phone/tablet *(✅)*
- iPhone/iPad *(⚠️; depends on management model)*  
- Chromebook/Kindle *(optional later — if you want Qustodio-like breadth)* citeturn1view0

### C) Policies (all rules live here)

#### C1) Screen time & routines
- Daily time limits + restricted times citeturn1view0  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- **Custom routines** (switch profiles quickly) citeturn1view0  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- Bedtime schedule + wake schedule  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- “School time” / limited phone functionality (Family Link style) citeturn0search3turn0news50  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

#### C2) Apps & games controls
- Block/allow apps and games; age ratings guidance citeturn1view0  
  *(PC✅ Mac✅ Android✅ iOS⚠️ Web✅)*
- Per‑app time limits  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- Install prevention / “require parent approval to install” (where supported) citeturn0search3  
  *(PC✅ Mac✅ Android✅ iOS⚠️ Web✅)*

#### C3) Web & content filtering
- Web category filtering + allow/alert/block categories citeturn1view0  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- SafeSearch / YouTube Restricted Mode enforcement (where supported)  
  *(PC✅ Mac✅ Android✅ iOS⚠️ Web✅)*
- **Porn/adult content blocking** (explicit toggle) citeturn1view0  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- Block VPN/proxy/circumvention (best effort, alert on detection)  
  *(PC✅ Mac✅ Android✅ iOS⚠️ Web—)*

#### C4) Social media controls
- Limit social media use (time or schedule) citeturn1view0  
  *(PC✅ Mac✅ Android✅ iOS⚠️ Web✅)*
- “Protect vs Block” model (Net Nanny style: apply content rules to supported services) citeturn0search2turn0search6  
  *(PC✅ Mac✅ Android✅ iOS⚠️ Web✅)*

#### C5) Communication safety (calls, SMS, messaging)
- Call & SMS monitoring + blocked numbers citeturn1view0turn0search12  
  *(PC— Mac— Android✅ iOS⚠️ Web✅)*
- Call/SMS monitoring note: some iOS support may require desktop bridge/setup citeturn1view0  
  *(PC✅ Mac✅ Android✅ iOS⚠️ Web✅)*
- Messaging/social monitoring with **alert-based AI** (bullying, grooming, self-harm, explicit content) citeturn0search1turn0search9turn0search13  
  *(PC✅ Mac✅ Android✅ iOS⚠️ Web✅)*

#### C6) Location & safety
- Location monitoring / family locator citeturn1view0turn0search3  
  *(PC— Mac— Android✅ iOS✅ Web✅)*
- Place labels (home/school) + arrive/leave notifications (geofencing) citeturn1view0  
  *(PC— Mac— Android✅ iOS✅ Web✅)*
- SOS / safety alerts (child triggers SOS; parent notified) citeturn1view0  
  *(PC— Mac— Android✅ iOS✅ Web✅)*
- “I’m OK” check‑in button (optional location share)  
  *(PC— Mac— Android✅ iOS✅ Web✅)*

#### C7) One‑tap “block everything / unblock” modes (your requirement)
- **Lockdown mode (Block everything)**: deny all apps + web except “Always Allowed” list  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- **Open mode (Unblock everything)**: disable restrictions temporarily (still log)  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- Always Allowed list (emergency contacts, school tools, parent-approved essentials)  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- Time-boxed exceptions (e.g., “unblock YouTube for 30 min”)  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

#### C8) AI-assisted grant/block (optional)
- “Request access” flow + AI suggested decision (duration/conditions/risk) citeturn1view0turn0search1  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- Explainable reasoning (“why suggested”) + parent must approve  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

### D) Requests (from child)
- Child requests: more time / unblock app / unblock site + reason  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- Parent approve/deny with conditions (duration, schedule, only on Wi‑Fi, etc.)  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

### E) Activity (timeline + reports)
- Activity timeline (web, apps, searches, YouTube) citeturn1view0  
  *(PC✅ Mac✅ Android✅ iOS⚠️ Web✅)*
- YouTube monitoring (views + searches) citeturn1view0turn0search6  
  *(PC✅ Mac✅ Android✅ iOS⚠️ Web✅)*
- Weekly/daily reports + alerts citeturn1view0  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

### F) Rewards & goals (optional)
- Time bank: earn minutes for chores/goals; parent controls exchange  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

---

# 4) Family Alerts & Reports (cross‑child)
- Unified alerts inbox (by severity; filter by child/device/category)
- Report scheduling (daily/weekly email reports) citeturn1view0  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- AI-powered alerts for concerning searches/conversations (Qustodio/Bark style) citeturn1view0turn0search1turn0search9  
  *(PC✅ Mac✅ Android✅ iOS⚠️ Web✅)*
- Audit log (policy changes: who/when/what)
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

---

# 5) Support & Safety
- Setup wizard (parent-friendly, no jargon, tooltips)
- Help center + troubleshooting
- Backup/restore family config
- Reset/wipe family (with safeguards)
- Safety resources + conversation starters (Qustodio-like) citeturn1view0  
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*

---

# 6) Admin / Advanced (optional)
- Anti-tamper & uninstall protection + alerts on tamper attempt  
  *(PC✅ Mac✅ Android✅ iOS⚠️ Web—)*
- Diagnostics bundle export (logs + device health snapshot)
  *(PC✅ Mac✅ Android✅ iOS✅ Web✅)*
- Network protection extras (DNS filtering, encrypted DNS controls, proxy detection)
  *(PC✅ Mac✅ Android✅ iOS⚠️ Web—)*

---

## Notes on competitor parity
- Qustodio’s public features set includes: filtering content/apps, monitoring activity, time limits & routines, family locator with place alerts, call & SMS tracking, and reports/alerts/SOS. citeturn1view0turn0search12  
- Bark emphasizes AI-powered safety alerts scanning texts/social platforms and media for risk categories. citeturn0search1turn0search9turn0search13  
- Net Nanny promotes “Protect” social services by applying filtering rules to supported apps/services. citeturn0search2turn0search6  
- Google Family Link highlights screen time limits, app controls, content restrictions, location checks, and school-time style controls. citeturn0search3turn0news50  
