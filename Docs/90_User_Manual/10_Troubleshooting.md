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

## Policy sync integrity + automatic rollback (Windows Kid Agent)

Safe0ne’s Kid Agent keeps a **last-known-good policy cache** so it can continue enforcing rules when the local server is unavailable.

If the cache becomes corrupted (e.g., disk write interruption), the agent will:

- mark a **policy cache corrupt** health signal (privacy-first)
- clear the corrupt cache and attempt to restore the previous backup

If the agent detects an enforcement/apply failure shortly after a policy update, it can **roll back to the previous cached policy** (best-effort) and surface a rollback signal.

Where to look:
- **Child → Devices/Health** (policy apply / rollback status)
- **Alerts / Activity** (events such as `policy_cache_corrupt`, `policy_rollback_applied`, `policy_apply_failed`)

## Collect a diagnostics bundle (for support)

If support asks for a **diagnostics bundle**, you can request and download a small ZIP from the UI.

Where:
- **Children → select a child → Devices tab → Diagnostics bundle**
- **Support & Safety** page (if available in navigation)

Flow:
1. Click **Request new bundle**.
2. Leave the Kid device running and online for 10–30 seconds.
3. Click **Download ZIP** once it appears.

Privacy notes:
- The bundle is **privacy-first** and excludes secrets (pairing tokens/auth state).
- It is intended for troubleshooting pairing and policy sync issues.
