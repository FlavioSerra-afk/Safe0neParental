using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Safe0ne.DashboardServer.ControlPlane;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.Reports;

/// <summary>
/// 16W9: Minimal SSOT-backed reports scheduler.
/// - Schedules are authored in Parent UI and stored in the Local Settings Profile (SSOT) under policy.reports.
/// - Execution state is stored under root.reportsState and must NOT bump policyVersion.
/// - When a digest runs, we append an activity event kind=report_digest so the UI can surface it.
///
/// This is intentionally small and local-first: no external delivery yet.
/// </summary>
public sealed class ReportSchedulerService : BackgroundService
{
    private readonly JsonFileControlPlane _cp;

    public ReportSchedulerService(JsonFileControlPlane cp)
    {
        _cp = cp;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start slightly delayed so the server can finish boot.
        try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); } catch { }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Tick(DateTimeOffset.UtcNow);
            }
            catch
            {
                // Best-effort scheduler; never bring down the server.
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch
            {
                // ignore
            }
        }
    }

    private void Tick(DateTimeOffset nowUtc)
    {
        var children = _cp.GetChildren();
        foreach (var c in children)
        {
            try
            {
                var childId = c.Id;
                if (childId == default) continue;
                ReportsDigest.TryRunDigestIfDue(_cp, childId, nowUtc, force: false);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
