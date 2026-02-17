# Device Health

Device Health shows whether a paired Kid device is currently online.

## Where to find it
Open **Children → select a child → Devices tab**.

## Status meanings
- **Online**: the device sent a heartbeat recently.
- **Offline**: the device has not sent a heartbeat recently.

## “Last seen”
“Last seen” is updated when the Kid agent sends an authenticated heartbeat.

## Troubleshooting
- If a device stays Offline, confirm the Kid agent is running.
- If the device is paired but heartbeats are Unauthorized, re-pair to refresh the device token.
