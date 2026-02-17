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
- If the device is paired but heartbeats are Unauthorized, re-pair to refresh the device token.


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

### Apply failures
If the Kid agent observes a best-effort **apply/enforcement error**, Safe0ne records a `policy_apply_failed` activity event and surfaces the last error under Device Health.

> This is an observability feature. Stronger self-repair/fail-closed behavior remains planned under EPIC-ANTITAMPER.
