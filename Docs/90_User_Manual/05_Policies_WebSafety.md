# Web Safety

## SafeSearch / Restricted toggles (implemented; persisted)
- Configure SafeSearch / Restricted Mode toggles.
- Settings persist via SSOT (local-first).

## Enforcement (partial)
Actual enforcement on-device may be best-effort depending on platform and browser.

## Categories (prototype)
- Categories can be set to **Allow**, **Alert**, or **Block**.
  - **Allow**: permitted.
  - **Alert**: tracked as a **Web alert** signal.
    - **Windows-first note:** current hosts-file enforcement cannot reliably “allow-but-alert” without deeper network hooks.
      In this prototype, **Alert** is surfaced as an alert signal and may still display the block page.
  - **Block**: restricted (best effort).

## Domains (prototype)
- **Allowed domains** override blocks.
- **Blocked domains** always block (best effort).
