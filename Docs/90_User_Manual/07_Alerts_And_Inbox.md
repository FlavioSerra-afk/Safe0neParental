# Alerts and Inbox

## Alerts inbox (implemented)
- Alerts are routed using persisted routing configuration.
- Inbox supports grouping and filters.
- Alerts can be acknowledged (ACK) using SSOT-backed endpoints.

## Notes
- If the Alerts self-test is failing due to a script parse error, treat it as a **blocking regression** and fix immediately.

## Activity-backed alerts
These alerts are derived from the **SSOT activity stream** (local-first) and appear in the Inbox:

- **Geofence enter/exit** events.
- **Circumvention detected** (VPN/proxy/private DNS / hosts protection failures) — best-effort.
- **Tamper / integrity issue** (agent not elevated, enforcement errors) — best-effort.

These are **edge-triggered**: they are emitted when a signal transitions from normal → flagged to avoid spamming.
