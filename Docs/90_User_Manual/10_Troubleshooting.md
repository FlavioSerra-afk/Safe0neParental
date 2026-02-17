# Troubleshooting

## WebView2 “Tracking Prevention blocked storage”
This warning can appear in WebView2 environments and indicates storage may be limited.
- Treat browser storage (e.g., localStorage) as **best-effort only**.
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
