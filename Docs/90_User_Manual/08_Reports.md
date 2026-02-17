# Reports

Reports provide a **privacy-first digest** of what happened for a child (e.g., policy-related events, safety alerts, and “report_digest” summaries).

## Where to find Reports
Open **Child Profile → Reports**.

## Scheduling
You can create one or more schedules per child.

What a schedule controls:
- **Frequency** (daily/weekly)
- **Time of day** (local time)
- **Enabled/disabled**

When a schedule is due, the DashboardServer generates a small report summary and writes it into the child’s **SSOT activity stream** as a `report_digest` event.

## Run now
Use **Run now** to generate a report immediately (useful for testing the pipeline end-to-end).

## Where generated reports appear
- A “Recent reports” list in the Reports surface.
- A `report_digest` item in the Activity feed (depending on your current UI configuration).

## Troubleshooting
- If Reports look empty, confirm the Kid Agent is running and sending heartbeats.
- If you see WebView2 “Tracking Prevention blocked storage”, that is expected; Reports are not dependent on localStorage.
