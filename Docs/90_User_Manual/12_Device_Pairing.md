# Device Pairing (Kid enrollment)

This feature links a **Kid device** to a **Child profile** so policies, requests, and heartbeats can be authenticated.

## Where to generate a pairing code (Parent)
1. Open **Children**.
2. Select a child profile.
3. Go to **Devices** tab.
4. Click **Generate pairing code**.
5. Keep the code handy (it expires).

## Where to enter the pairing code (Kid)
On the Kid device, open the Safe0ne Child UX and go to:

- **Today → Enter pairing code**, or directly: `http://127.0.0.1:8771/pair`

Enter the code and press **Pair**.

## What happens after pairing
- The agent receives a **device token** and caches it locally.
- The agent also stores the **current child id binding** so it knows which child profile to authenticate as on restart.
- Heartbeats and agent endpoints will include the token automatically.

## Troubleshooting
- If pairing fails, verify the code is still active in the Parent app (Devices tab).
- Ensure the DashboardServer is running locally and reachable from the Kid device environment.
- If the device was previously paired to a different child, re-pairing will update the current child binding.
