# Alerts and Inbox

## Alerts inbox (implemented)
- Alerts are routed using persisted routing configuration.
- Inbox supports grouping and filters.
- Alerts can be acknowledged (ACK) using SSOT-backed endpoints.

## Notes
- If the Alerts self-test is failing due to a script parse error, treat it as a **blocking regression** and fix immediately.


## Device integrity alerts (tamper / circumvention)

Safe0ne surfaces two high-signal integrity events:

- **Possible bypass attempt** (circumvention): VPN/proxy/public DNS/hosts protection failures.
- **Agent health issue** (tamper): agent not running elevated, or enforcement errors.

### Where it shows
- **Alerts Inbox** → grouped under the child.

### How to control it
On the child’s **Policy** page, under **Device signals → Device integrity**, you can toggle:

- **Detect tamper / enforcement health**
- **Surface tamper issues in Alerts**
- **Surface circumvention issues in Alerts**

If “Surface … in Alerts” is off, these signals are still collected best-effort but will not appear as Alerts Inbox items.
