# Screen Time Policies

## What you can configure (Parent app)
You can configure per-child screen-time limits under the **Policies** tab:

- **Daily limit (minutes)**
- **Grace minutes** (extra minutes after the limit before Lockdown)
- **Warning thresholds** (e.g., `5,1` minutes remaining)
- Optional **per-day overrides** (Mon..Sun)

These values are persisted into SSOT and bump the child **policyVersion** on save.

## What happens on the Kid device (Windows agent v1)
When a daily limit is enabled:

- The agent tracks **privacy-first active screen time** (idle-filtered).
- When remaining time crosses a configured threshold (e.g. 5m / 1m), a **warning screen** is shown.
- When the budget is depleted (including grace), enforcement switches to **Lockdown** and the kid sees a **Time limit reached** blocked screen.

### Requesting more time
From the blocked screen (or warning screen), the kid can tap **Request +15 minutes**.

Additionally, when the agent first detects that the daily budget is depleted, it will **auto-queue a “More time” request** (best-effort) so the parent inbox becomes actionable even if the kid does not open the request UI.

## Parent reporting
The parent UI shows a rollup from the agent status:

- **Used / Remaining** minutes
- **Depleted** status

A quick summary is available in the **Reports** page under the **Screen time** panel.

## Notes / scope
- This is a Windows prototype slice. Other platforms may remain **partial** until their enforcement engines land.
- The data is a rollup only (minutes used/remaining) to stay privacy-first.
