using System;
using System.IO;
using Xunit;

namespace Safe0ne.Shared.Tests;

public sealed class DashboardReportsMarkerTests
{
    [Fact]
    public void Reports_Wiring_And_Module_Markers_Are_Present()
    {
        var repoRoot = FindRepoRoot();

        var routerPath = Path.Combine(repoRoot, "src", "Safe0ne.DashboardServer", "wwwroot", "app", "router.js");
        var reportsModulePath = Path.Combine(repoRoot, "src", "Safe0ne.DashboardServer", "wwwroot", "app", "features", "reports.js");

        Assert.True(File.Exists(routerPath), $"Expected file not found: {routerPath}");
        Assert.True(File.Exists(reportsModulePath), $"Expected file not found: {reportsModulePath}");

        var routerJs = NormalizeNewlines(File.ReadAllText(routerPath));
        var reportsJs = NormalizeNewlines(File.ReadAllText(reportsModulePath));

        // Router markers: route title exists and renderReports delegates to Safe0neReports.
        Assert.Contains("Family Alerts & Reports", routerJs, StringComparison.Ordinal);
        Assert.Contains("function renderReports", routerJs, StringComparison.Ordinal);

        var renderReportsStart = routerJs.IndexOf("function renderReports", StringComparison.Ordinal);
        Assert.True(renderReportsStart >= 0, "renderReports function marker not found.");

        var renderReportsRegion = routerJs.Substring(renderReportsStart, Math.Min(2500, routerJs.Length - renderReportsStart));
        Assert.Contains("window.Safe0neReports", renderReportsRegion, StringComparison.Ordinal);
        Assert.Contains("renderReports()", renderReportsRegion, StringComparison.Ordinal);
        Assert.Contains("_renderReportsFallback", renderReportsRegion, StringComparison.Ordinal);

        // Module markers: Reports module renders the UI shell and uses Alerts module.
        Assert.Contains("window.Safe0neReports", reportsJs, StringComparison.Ordinal);
        Assert.Contains("Family Alerts & Reports", reportsJs, StringComparison.Ordinal);
        Assert.Contains("alerts-inbox", reportsJs, StringComparison.Ordinal);
        Assert.Contains("Show acknowledged", reportsJs, StringComparison.Ordinal);
        Assert.Contains("Safe0neAlerts.buildAlerts", reportsJs, StringComparison.Ordinal);
    }

    private static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n");

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
