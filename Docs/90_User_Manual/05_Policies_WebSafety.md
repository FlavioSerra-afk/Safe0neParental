# Web Safety

## SafeSearch / Restricted toggles (implemented; persisted)
- Configure SafeSearch / Restricted Mode toggles.
- Settings persist via SSOT (local-first).

## Enforcement (partial)
Actual enforcement on-device may be best-effort depending on platform and browser.


## Web alerts (Inbox)

When **blocked domains** are hit on the child device, the Kid Agent reports a daily **Web Alerts Today** count to the Local Control Plane. The Alerts Inbox will surface a "Web safety" alert when this count is > 0.

- This is **privacy-first**: only aggregate counts and top blocked domains are reported (no full browsing history).
- To reduce alerts, adjust **Blocked domains** and **Category rules**, or add an exception in **Allowed domains**.
