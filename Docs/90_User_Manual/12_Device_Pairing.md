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

## Unpair
In **Devices tab**, click **Unpair** for the device.
