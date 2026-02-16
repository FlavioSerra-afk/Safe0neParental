using System;
using System.IO;
using Xunit;

namespace Safe0ne.Shared.Tests;

/// <summary>
/// Marker tests to guard against accidental regressions in DashboardServer UI hot files
/// (e.g. router.js) due to patch ZIP extraction overwrites.
///
/// These tests are intentionally lightweight: they assert the presence and ordering of
/// key feature markers, aligned to ADR-0003 "Modular DashboardServer UI feature modules".
/// </summary>
public sealed class DashboardUiMarkerTests
{
    [Fact]
    public void IndexHtml_Loads_AlertsModule_Before_Router()
    {
        var repoRoot = FindRepoRoot();
        var indexPath = Path.Combine(repoRoot, "src", "Safe0ne.DashboardServer", "wwwroot", "index.html");

        Assert.True(File.Exists(indexPath), $"Expected file not found: {indexPath}");

        var html = File.ReadAllText(indexPath);

        var alertsScript = "/app/features/alerts.js";
        var routerScript = "/app/router.js";

        var iAlerts = html.IndexOf(alertsScript, StringComparison.OrdinalIgnoreCase);
        var iRouter = html.IndexOf(routerScript, StringComparison.OrdinalIgnoreCase);

        Assert.True(iAlerts >= 0, $"index.html must reference {alertsScript}");
        Assert.True(iRouter >= 0, $"index.html must reference {routerScript}");
        Assert.True(iAlerts < iRouter, $"{alertsScript} must be loaded before {routerScript}");
    }

    [Fact]
    public void RouterJs_Delegates_Alerts_To_Safe0neAlerts_Module()
    {
        var repoRoot = FindRepoRoot();
        var routerPath = Path.Combine(repoRoot, "src", "Safe0ne.DashboardServer", "wwwroot", "app", "router.js");

        Assert.True(File.Exists(routerPath), $"Expected file not found: {routerPath}");

        var js = File.ReadAllText(routerPath);

        // Feature markers (thin router.js delegating to module)
        Assert.Contains("Safe0neAlerts.buildAlerts", js, StringComparison.Ordinal);
        Assert.Contains("Safe0neAlerts.renderAlertActions", js, StringComparison.Ordinal);
        Assert.Contains("Safe0neAlerts.bindAlertsList", js, StringComparison.Ordinal);
        Assert.Contains("Safe0neAlerts.isAcked", js, StringComparison.Ordinal);

        // UI marker: show/hide acknowledged toggle.
        Assert.Contains("Show acknowledged", js, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        // We locate the repo root by walking up from the test binary output and finding the solution file.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var sln = Path.Combine(dir.FullName, "Safe0neParental.sln");
            if (File.Exists(sln))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repo root (Safe0neParental.sln) from test output directory.");
    }
}
