# Device Pairing

Pairing connects a Kid device (agent + Kid UX) to a specific child profile.

Pairing in this repo is currently **code-based** (local-first).

## Generate a pairing code (Parent)
1. Open **Children → select a child → Devices tab**.
2. Click **Generate pairing code**.
3. Copy the code.

## Enter the pairing code (Kid)
1. On the Kid device, open **Kid UX**.
2. Go to **/pair**.
3. Enter the pairing code and submit.

If successful, the Kid agent receives a **device token** and persists it so future heartbeats authenticate.

## Device token expiry
Device tokens have a time-to-live (TTL). When a token expires, the Kid device will show as **Unauthorized** until you re-pair.

You can see the token expiry time in **Children → Devices tab**.

## Revoke token (security)
If you suspect a Kid device token has been copied or the device is compromised, you can revoke it without deleting the device entry:

1. Open **Children → select a child → Devices tab**.
2. Click **Revoke token**.

After revocation, the Kid device will become **Unauthorized** and must be re-paired to regain access.

## Unpair
In **Devices tab**, click **Unpair** for the device.

> Tip: Unpair removes the device entry from this PC’s local control plane. Revoking a token keeps the entry but blocks authentication.
