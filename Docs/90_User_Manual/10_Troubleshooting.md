# Troubleshooting

## WebView2 “Tracking Prevention blocked storage”
This warning can appear in WebView2 environments and indicates storage may be limited.
- Treat browser storage (e.g., localStorage) as **best-effort only**.
- Primary persistence must use SSOT/local-first storage.

## If the UI seems stale
- Restart the Parent App.
- Re-run self-tests and check marker tests.
