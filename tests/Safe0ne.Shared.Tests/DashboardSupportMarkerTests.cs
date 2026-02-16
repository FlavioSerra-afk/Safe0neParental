using System;
using System.IO;
using Xunit;

namespace Safe0ne.Shared.Tests;

public sealed class DashboardSupportMarkerTests
{
    [Fact]
    public void IndexHtml_Loads_SupportModule_Before_Router()
    {
        var repoRoot = FindRepoRoot();
        var indexHtmlPath = Path.Combine(repoRoot, "src", "Safe0ne.DashboardServer", "wwwroot", "index.html");
        Assert.True(File.Exists(indexHtmlPath), $"Expected file not found: {indexHtmlPath}");

        var html = File.ReadAllText(indexHtmlPath);

        var supportIdx = html.IndexOf("/app/features/support.js", StringComparison.OrdinalIgnoreCase);
        var routerIdx = html.IndexOf("/app/router.js", StringComparison.OrdinalIgnoreCase);

        Assert.True(supportIdx >= 0, "Expected index.html to reference /app/features/support.js");
        Assert.True(routerIdx >= 0, "Expected index.html to reference /app/router.js");
        Assert.True(supportIdx < routerIdx, "Expected /app/features/support.js to load before /app/router.js");
    }

    [Fact]
    public void RouterJs_Delegates_Support_To_Safe0neSupport()
    {
        var repoRoot = FindRepoRoot();
        var routerPath = Path.Combine(repoRoot, "src", "Safe0ne.DashboardServer", "wwwroot", "app", "router.js");
        Assert.True(File.Exists(routerPath), $"Expected file not found: {routerPath}");

        var js = File.ReadAllText(routerPath);

        Assert.Contains("Safe0neSupport", js, StringComparison.Ordinal);
        Assert.True(
            js.Contains("Safe0neSupport.renderSupport", StringComparison.Ordinal) ||
            js.Contains("Safe0neSupport.render", StringComparison.Ordinal),
            "Expected router.js to delegate Support rendering to Safe0neSupport (e.g., Safe0neSupport.renderSupport(...))"
        );
    }

    private static string FindRepoRoot()
    {
        // Walk up from test output directory until we find the solution file.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "Safe0neParental.sln");
            if (File.Exists(sln))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root (Safe0neParental.sln not found walking up from test output).");
    }
}
