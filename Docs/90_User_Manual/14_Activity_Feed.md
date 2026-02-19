# Activity Feed (Local Mode)

Updated: 2026-02-18

The Activity Feed shows **recent events** emitted by the child device and the local server (DashboardServer).  
It is intended for **debugging, reports, and audit-style visibility**.

## Where to find it
- **Parent Dashboard → Children → select a child → Activity tab**

## What appears in Activity
Examples of common activity items:
- `report_digest` — report scheduler “run now” or scheduled digest wrote an entry
- `device_online` / `device_offline` — device status transitions (when enabled)
- `geofence_enter` / `geofence_exit` — location boundary events (when enabled)
- `policy_applied` / `policy_apply_failed` — policy runtime signals (when enabled)

> The exact list will expand over time as Kid Agent enforcement becomes fully implemented.

## Retention (SSOT behavior)
Activity is stored in the **Local SSOT** as an append-only stream per child, with conservative retention:
- **Retention window:** 30 days (prunes older events if timestamps exist)
- **Max events:** 2000 newest events kept per child

## Export
From the Activity tab you can click **Export** to open the current activity envelope in a new tab/window.

There is a JSON export endpoint intended for later “diagnostics bundle” packaging:

- `GET /api/local/children/<childId>/activity/export`

It returns a stable envelope:
- `childId`
- `exportedAtUtc`
- `retentionDays`
- `maxEvents`
- `events[]`

This is currently **JSON-only** (no ZIP bundle yet).
