using System;
using System.IO;
using Xunit;

namespace Safe0ne.Shared.Tests;

public sealed class DashboardRequestsMarkerTests
{
    [Fact]
    public void IndexHtml_Loads_RequestsModule_Before_Router()
    {
        var repoRoot = FindRepoRoot();
        var indexHtmlPath = Path.Combine(repoRoot, "src", "Safe0ne.DashboardServer", "wwwroot", "index.html");
        Assert.True(File.Exists(indexHtmlPath), $"Expected file not found: {indexHtmlPath}");

        var html = File.ReadAllText(indexHtmlPath);

        var requestsIdx = html.IndexOf("/app/features/requests.js", StringComparison.OrdinalIgnoreCase);
        var routerIdx = html.IndexOf("/app/router.js", StringComparison.OrdinalIgnoreCase);

        Assert.True(requestsIdx >= 0, "Expected index.html to reference /app/features/requests.js");
        Assert.True(routerIdx >= 0, "Expected index.html to reference /app/router.js");
        Assert.True(requestsIdx < routerIdx, "Expected /app/features/requests.js to load before /app/router.js");
    }

    [Fact]
    public void RouterJs_Delegates_Requests_To_Safe0neRequests()
    {
        var repoRoot = FindRepoRoot();
        var routerPath = Path.Combine(repoRoot, "src", "Safe0ne.DashboardServer", "wwwroot", "app", "router.js");
        Assert.True(File.Exists(routerPath), $"Expected file not found: {routerPath}");

        var js = File.ReadAllText(routerPath);

        Assert.Contains("Safe0neRequests", js, StringComparison.Ordinal);
        Assert.True(
            js.Contains("Safe0neRequests.renderRequests", StringComparison.Ordinal) ||
            js.Contains("Safe0neRequests.render", StringComparison.Ordinal),
            "Expected router.js to delegate Requests rendering to Safe0neRequests (e.g., Safe0neRequests.renderRequests(...))"
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
