# DevTools and Self-Tests

## Hidden DevTools unlock (implemented)
DevTools are intentionally hidden in normal use.

- Use the **hidden unlock gesture** (7x tap) to reveal DevTools.
- Toggle DevTools off to hide again; the gesture should re-enable.

## Self-test badge and marker tests
- The router/self-test badge indicates health of critical UI pipelines.
- Marker/guard tests must remain green (additive only).

### SSOT Purity (Self-Test)
This self-test validates that when the Local API is reachable, the UI does not write domain state (children/profiles/policy) to browser storage.
- Allowed: preferences keys only (filters/toggles/theme/devtools unlock)
- Disallowed: any persistence of children/profile/policy data
