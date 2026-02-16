# Audit Log (Policy & Settings Changes)

This page describes the **Audit Log** feature that records **append-only** entries whenever the Parent app saves policy or settings that affect a child.

## Where to find it
In the Parent app:

- **Admin → Advanced → Audit Log**
- Choose a **Child** and review the most recent entries.

## What gets logged
The Audit Log records changes when policy/settings are saved through the local API (local-first):

- Policy save operations (e.g., Screen Time, App Limits, Web Safety, Geofences, Alerts routing)
- Each entry is **append-only** and ordered.

> It is designed to support troubleshooting, transparency (“what changed?”), and future tamper-evidence improvements.

## Entry fields (what you will see)
Each audit entry is a JSON object with the following common fields:

- `occurredAtUtc` — UTC timestamp (ISO 8601)
- `actor` — who performed the change (typically `parent-local`)
- `scope` — logical area (typically `policy`)
- `action` — operation (e.g., `policy.save`, `policy.patch`)
- `beforeHashSha256` / `afterHashSha256` — best-effort hashes to indicate a change was applied
- `prevHash` / `hash` — a lightweight hash chain for tamper-evident ordering
- `details` — additional structured fields when available

## Retention
- The log is retained on-device (local-first).
- Entries are pruned by retention policy (time-based) and capped by a max item count to avoid unbounded growth.

## Troubleshooting
- If the Audit Log is empty, confirm you have saved a policy change for that child.
- If you see WebView2 storage warnings, this does **not** affect the audit log (audit is persisted in the Local Control Plane, not browser storage).
