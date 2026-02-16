using System;
using System.IO;
using Xunit;

namespace Safe0ne.Shared.Tests;

/// <summary>
/// Marker tests to guard against accidental regressions for Dashboard route modularization
/// per ADR-0003 (feature modules loaded before router.js).
/// </summary>
public sealed class DashboardDashboardMarkerTests
{
    [Fact]
    public void IndexHtml_Loads_DashboardModule_Before_Router()
    {
        var repoRoot = FindRepoRoot();
        var indexPath = Path.Combine(repoRoot, "src", "Safe0ne.DashboardServer", "wwwroot", "index.html");

        Assert.True(File.Exists(indexPath), $"Expected file not found: {indexPath}");

        var html = File.ReadAllText(indexPath);

        var moduleScript = "/app/features/dashboard.js";
        var routerScript = "/app/router.js";

        var iModule = html.IndexOf(moduleScript, StringComparison.OrdinalIgnoreCase);
        var iRouter = html.IndexOf(routerScript, StringComparison.OrdinalIgnoreCase);

        Assert.True(iModule >= 0, $"index.html must reference {moduleScript}");
        Assert.True(iRouter >= 0, $"index.html must reference {routerScript}");
        Assert.True(iModule < iRouter, $"{moduleScript} must be loaded before {routerScript}");
    }

    [Fact]
    public void RouterJs_Delegates_Dashboard_To_Safe0neDashboard_Module()
    {
        var repoRoot = FindRepoRoot();
        var routerPath = Path.Combine(repoRoot, "src", "Safe0ne.DashboardServer", "wwwroot", "app", "router.js");

        Assert.True(File.Exists(routerPath), $"Expected file not found: {routerPath}");

        var js = File.ReadAllText(routerPath);

        Assert.Contains("Safe0neDashboard.renderDashboard", js, StringComparison.Ordinal);
        Assert.Contains("Safe0neDashboard module render failed", js, StringComparison.Ordinal);
        Assert.Contains("_renderDashboardFallback", js, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
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
