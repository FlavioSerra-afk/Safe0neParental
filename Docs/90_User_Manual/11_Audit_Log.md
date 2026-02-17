# Audit Log

The Audit Log is an **append-only** record of policy/settings changes made in the Parent UI.

## Where to find it
Open **Admin → Audit Log**.

## What it shows
- Timestamp (UTC)
- Child
- Action (e.g., “policy saved”, “routing saved”)
- A small redacted summary (privacy-first)

## Notes
- The audit stream is stored in the **Local Control Plane** (SSOT).
- The viewer is intended for transparency/debugging; it does not replace long-term compliance logging.
