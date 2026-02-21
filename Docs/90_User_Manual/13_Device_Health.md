# Device Health

Device Health shows whether a paired Kid device is currently online.

## Where to find it
Open **Children → select a child → Devices tab**.

## Status meanings
- **Online**: the device sent a heartbeat recently.
- **Offline**: the device has not sent a heartbeat recently.

## “Last seen”
“Last seen” is updated when the Kid agent sends an authenticated heartbeat.

## Troubleshooting
- If a device stays Offline, confirm the Kid agent is running.
- If the device is paired but heartbeats are Unauthorized, the token may be **revoked** or **expired**. Re-pair to issue a fresh token (or unpair + pair again).

## Policy sync health (counters)

Safe0ne surfaces **privacy-first counters** to help diagnose sync issues:

- **Heartbeat failures**: consecutive heartbeat attempts that did not return HTTP 200.
- **Policy fetch failures**: consecutive ticks where the agent could not fetch a live policy surface (it may still enforce from the last cached policy).
- **Auth rejected**: the server rejected the device token (typically revoked/expired).

### Where to see it
Open **Children → select a child → Devices tab → Policy sync health**.

### What to do
- If **Auth rejected** is Yes → re-pair the device (or unpair + pair).
- If failures keep increasing → check the DashboardServer is running, then export diagnostics.


## Status reasons (Needs attention)
- **Never seen**: paired but no authenticated heartbeat yet.
- **Stale**: last seen is older than the offline threshold (currently ~3 minutes in local mode).
- **Auth issue**: the server observed recent unauthorized heartbeats for this child (token missing/invalid). This is best-effort.

## Online/offline transitions
When a device transitions **Online ↔ Offline**, a **Device** activity event is recorded in the child Activity feed.
## Tamper and circumvention signals (best-effort)

The Kid Agent reports **coarse, privacy-first** signals to help you understand when enforcement may be degraded.

### Signals currently reported
- **Agent not elevated**: the agent is running without admin rights (Windows). Some enforcement actions may fail.
- **Recent enforcement error**: the agent recently encountered an enforcement failure.

### Where it appears
- **Activity**: these signals are emitted as activity events so they appear in the child Activity feed (and may be surfaced in Alerts depending on routing).
- **Troubleshooting**: see `User Manual → Troubleshooting` for common fixes.

> Note: this is not yet “fail-closed”. Self-repair and stronger anti-tamper measures are tracked under EPIC-ANTITAMPER and remain planned.


## Policy versions & apply status

Safe0ne tracks two related policy values:

- **Configured policy version**: the version stored in the Local Control Plane (SSOT). It increments whenever you save policy changes.
- **Last applied policy version**: the last policy version the Kid agent reports it actually applied.

### Why they can differ
- The device is **offline** and has not received the latest policy yet.
- The Kid agent is enforcing from its **last-known-good cache** (offline-first fallback).

### Watchdog (Pending / Overdue)
When the configured policy version is newer than the last applied version, Safe0ne surfaces:

- **Pending**: mismatch detected (expected during brief offline windows).
- **Overdue**: mismatch persisted beyond the watchdog threshold (default ~10 minutes).

Overdue typically means the device has been offline for a while, pairing token is invalid/expired, or the agent is unhealthy.

> Diagnostics: for local testing, the watchdog threshold can be overridden via `SAFE0NE_POLICY_WATCHDOG_MINUTES` (or `SAFE0NE_POLICY_WATCHDOG_SECONDS` in test environments).

### Apply failures
If the Kid agent observes a best-effort **apply/enforcement error**, Safe0ne records a `policy_apply_failed` activity event and surfaces the last error under Device Health.

> This is an observability feature. Stronger self-repair/fail-closed behavior remains planned under EPIC-ANTITAMPER.

### Rollback to last known good (recommended)
When an apply failure is reported, Safe0ne can recommend a rollback target:

- **Last known good**: the newest policy version that the device reported as successfully applied.
- **Recommended rollback**: shown only when an apply failure occurs and a known-good snapshot exists.

In the Child → **Device integrity → Policy apply** panel, you may see a **Rollback to last known good** button.

What it does:
- Restores the policy settings from the last known good snapshot.
- Bumps the configured policy version (monotonic) so the Kid agent re-syncs and applies it.

If the button is not shown, either no failure was reported, or Safe0ne does not yet have a snapshot for that version.
