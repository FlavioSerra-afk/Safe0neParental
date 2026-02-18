# Troubleshooting

## WebView2 “Tracking Prevention blocked storage”
This warning can appear in WebView2 environments and indicates storage may be limited.
- Browser storage (e.g., localStorage) is **preferences-only** and best-effort. Do not rely on it for children/profiles/policies.
- Primary persistence must use SSOT/local-first storage.

## If the UI seems stale
- Restart the Parent App.
- Re-run self-tests and check marker tests.

## Agent not elevated (tamper signal)

If you see activity like **“Agent is not running elevated”**:

- Ensure the Kid Agent is installed and started with the required privileges (Windows service / scheduled task depending on deployment mode).
- Re-run the Kid Agent as Administrator during development.
- Verify Windows UAC / policies are not blocking elevation.

This is a **best-effort** signal; it indicates enforcement actions may be limited until elevation is restored.

## Local service offline
If you see a message like **“Local service offline”**:
- The DashboardServer (localhost) is not reachable.
- The UI may still show cached *preferences* (filters/toggles), but it will not store or edit children/policies without SSOT.

What to do:
- Restart the Parent App (it should start DashboardServer).
- Verify the DashboardServer process is running.
- Re-run DevTools self-tests (Health / SSOT purity) once available.
