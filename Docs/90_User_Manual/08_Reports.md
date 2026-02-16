# Reports

## Scheduling (local-first)

Safe0ne supports **local reports scheduling** (on the parent device) so you can generate periodic digest summaries based on recent activity.

### Where to find it
- **Reports → Scheduling** (Parent App)

### What you can do today (implemented)
- Enable/disable scheduled digest generation per child.
- Choose:
  - Frequency (`off` / `daily` / `weekly`)
  - Local time for generation
  - Weekly day (for weekly digests)
- Use **Run now** to generate a digest immediately (for testing).

### What happens when a report runs
- The DashboardServer reads the child's recent activity from SSOT (local activity stream).
- A digest summary is generated and stored back into SSOT:
  - `reportsState.lastDigestAtUtc`
  - `reportsState.lastDigestSummary`
- A corresponding **activity entry** is appended so it can be surfaced in Reports/Alerts views.

### Notes / limitations (planned)
- Delivery channels (email/push/export) are **planned**; today, generation is local-first and viewable in the UI.
- Retention for historical digests will be added later; currently we keep the latest summary state plus the activity entry.
