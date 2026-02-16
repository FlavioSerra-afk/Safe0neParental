# Safe0ne Parental — UI/UX Specification (Parent App, Local Web Dashboard)
Updated: 2026-02-02
Status: **LAW (implementation must conform)**
Scope: Parent App **local web dashboard** (served by local host; rendered in Windows WebView2 or browser).
Visual goal: **soft, calm, modern dashboard** with a **left “dock” nav rail**, **rounded cards**, and an optional **right-side utility panel** (inspired, not copied).

---

## 0) UX rules (non-negotiable LAW)

1) **No jargon UX**
- Any technical term must have a plain-language label and/or tooltip.
- Tooltips: 1–3 short sentences; “what it does” + “why it matters”.

2) **No overflow / no bleeding**
- At any window size, nothing overlaps or clips outside the viewport.
- Only **vertical scroll** in the main content area.
- Horizontal scroll is a bug.

3) **Fast recognition (always visible in header)**
- Signed in?
- Protection running?
- Which children are protected?
- What should I do next?

4) **Additive-only UX**
- Do not break routes or existing interactions.
- UI refactors must preserve existing behavior.

5) **No onboarding wizard**
- Replace with a calm **dashboard checklist** + contextual empty-states.

---

## 1) Information Architecture (main rail menu)
This is the **main rail menu** and must remain compact:

### Sidebar (compact)
- **Dashboard**
- **Parent Profile**
- **Children** *(each child contains Devices + Policies + Activity)*
- **Family Alerts & Reports**
- **Support & Safety**
- **Admin / Advanced**

Everything else is served through:
- Child Profile internal tabs (secondary navigation)
- Page-level sub-navigation (tabs within a page)
- Progressive disclosure (“Advanced…” accordions)
- Hidden Developer entry (inside Admin/Advanced)

---

## 2) Layout system (WebView-first, resizable windows)

### Breakpoints
- **Wide:** ≥ 1200px
- **Standard:** 900–1199px
- **Compact:** 600–899px
- **Tiny:** < 600px (fallback)

### Global structure
- Left nav rail (fixed, never overlays content)
- Top header strip (inside page content)
- Main content area (vertical scroll only)
- Optional right utility panel (Dashboard, Child Overview)

### No-overflow engineering requirements
- Prefer CSS grid with `minmax(0, 1fr)` and card grids with `auto-fit`.
- Apply `min-width: 0` to flex/grid children.
- Avoid fixed widths inside cards; use responsive containers.
- Ensure rail width is stable; main content uses `flex: 1` and scrolls vertically.

---

## 3) Visual direction (to achieve the target look/feel)

### Aesthetic
- Light, airy, rounded corners.
- Gentle gradients, soft shadows, calm spacing.
- Cards use consistent structure: **Title → explanation → status → action**.

### Brand palette (unique to Safe0ne Parental)
- Primary: Blue → Teal gradient
- Secondary: Green (OK/safe)
- Accent: Warm yellow/orange (highlights only)
- Neutral: whites/soft grays (default), deep navy/charcoal (dark mode)

### Theme
- Default: Light
- Optional: Dark (reduced glare)

---

## 4) Left nav rail (“dock” styling)

### Rail styling rules
- Background: vertical brand gradient, large rounded outer corners.
- Items: icon + label (label below icon for “dock” feel).
- Selected item: white rounded “pill” background (no overlap into content).
- Hover: subtle highlight; Focus: visible keyboard focus ring.

### Responsive behavior
- Wide/Standard: icon + label
- Compact: icon + shorter label / tighter spacing
- Tiny: icon-only + tooltips; rail stays accessible

---

## 5) Top header strip (status-first)

Right side:
- Theme toggle
- Notifications
- Profile/avatar

Always visible **status chips** (text + icon, not color-only):
- **Protection:** On / Needs attention / Off
- **Local service:** Running / Not running
- Optional: **Devices online:** e.g., “3/4 online”

---

## 6) Component rules (toggles-first + clarity)

### Switch / Toggle rule
Use a switch only for **true on/off settings** where the action takes effect **immediately**.
If the setting is multi-choice or needs review:
- segmented control
- radio group
- checkbox list

### Progressive disclosure (Advanced)
Default UI shows essential controls; advanced controls appear on demand (“Advanced…”).

---

## 7) Page templates (consistent everywhere)

### Standard page header
- H1 title
- 1–2 sentence explanation (plain language)
- Primary action (top-right) if relevant
- Optional “Last updated”

### Cards are primary containers
Use card anatomy consistently: header + body + actions.

---

## 8) Dashboard (confidence-first)

### Layout (Wide/Standard)
Two-column dashboard grid:
- **Left main column:** hero banner + weekly cards + key panels
- **Right utility column:** quick controls + alerts summary + trend chart

### A) Hero banner
- “Hello, {ParentName}”
- Subtext (dynamic):
  - “Protection is running on this computer.”
  - OR “Needs attention: 1 device is offline.”

### B) Quick controls (right panel)
**One-tap Modes** (segmented):
- Lockdown (Block everything)
- Open (Unblock)
- Homework
- Bedtime

**Instant toggles** (examples)
- “New app installs need approval”
- “Block adult content”
- “Warn me if protection is disabled”
- “Quiet hours notifications”

### C) Weekly snapshot cards
- Screen time (week)
- Top apps
- Top sites
- Blocked attempts
- Requests pending

### D) Trend chart
- Screen time trend OR blocked attempts trend (Mon–Sun)

### E) “What’s next” checklist (replaces wizard)
- 3–6 steps max
- Each: title + 1 line + CTA
- Examples:
  - Add a child
  - Pair a device
  - Set bedtime
  - Review web rules

---

## 9) Parent Profile (all parent settings live here)
Cards:
- Account & security
- Household settings (timezone, templates)
- Notifications
- Privacy & exports
- Recovery tools (danger zone)

Pattern: toggles for simple switches; “Advanced” for granular.

---

## 10) Children (core area)

### Children list page
Card per child:
- Name + avatar/initial
- Status chips: Protected / Needs pairing / Device offline
- Primary CTA: Open profile
- Secondary CTA: Pair device

### Child Profile (single place for all child policies)
Internal tabs (NOT in main rail):
- **Overview**
- **Devices**
- **Policies**
- **Requests**
- **Activity**

#### Policies tab (ALL rules live here)
**Simple (default)**
- One-tap modes
- Daily time limit toggle + slider
- Bedtime toggle + time range
- Web: adult toggle + category grid
- Apps: “new installs need approval” toggle

**Advanced (collapsed)**
- Per-app limits
- Per-site allow/block list
- Weekday schedules
- Circumvention detection tuning (best-effort + alerts)
- Per-device overrides (later if needed)

---

## 11) Family Alerts & Reports (cross-child)
Tabs:
- Alerts inbox (calm, actionable)
- Reports (week overview, trends, top apps/sites)

---

## 12) Support & Safety
- Help & troubleshooting
- Safety resources
- Backup/restore family config
- Reset/wipe family (safeguarded)

---

## 13) Admin / Advanced (optional)
Visible:
- Anti-tamper settings (plain-language)
- Diagnostics export
- Network extras (where feasible)

Hidden Developer:
- Entry: click version 7 times → dev passcode (or DEV build flag)
- Warning banner: “Developer settings can break protection.”

---

## 14) Accessibility & usability
- Comfortable click targets; avoid icon-only actions without labels.
- Keyboard navigation: logical tab order + visible focus.
- Never rely on color alone for state.

---

## 15) Design tokens (required)
All styling references tokens (CSS variables), not random hex values:
- Color: bg/surface/text/border
- Brand: gradient stops + accent
- Semantic: success/warning/danger/info
- Radius, elevation, spacing, typography

---

## 16) Asset checklist
- Nav icons: Dashboard, Parent Profile, Children, Family Alerts & Reports, Support & Safety, Admin/Advanced
- Child tabs icons: Overview, Devices, Policies, Requests, Activity
- Utility icons: refresh/info/success/warn/error/offline/wifi/lock/time/web/apps/back/close
- Empty states: no children/no devices/no requests/no alerts/no activity/reports not ready

---

## 17) UI QA acceptance checklist
- No horizontal scroll at any size.
- Nav rail never overlaps content.
- Cards wrap cleanly in Compact/Tiny.
- Toggles are clear and immediate.
- Tooltips readable; focus visible; copy is non-jargon.
