using System;
using System.IO;
using Xunit;

namespace Safe0ne.Shared.Tests
{
    public class DashboardChildrenMarkerTests
    {
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Safe0neParental.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Could not locate repo root (Safe0neParental.sln).");
        }

        private static string ReadText(string repoRoot, params string[] rel)
        {
            var path = Path.Combine(new[] { repoRoot }.Concat(rel).ToArray());
            if (!File.Exists(path))
                throw new FileNotFoundException($"Expected file not found: {path}");
            // Normalize newlines so substring checks don't depend on CRLF vs LF.
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        [Fact]
        public void IndexHtml_Loads_ChildrenModule_Before_Router()
        {
            var repoRoot = FindRepoRoot();
            var html = ReadText(repoRoot, "src", "Safe0ne.DashboardServer", "wwwroot", "index.html");

            // If modular children module exists, it must be loaded before router.js.
            // If a future refactor removes the module, this test will still pass as long as the app is consistent.
            var childrenIdx = html.IndexOf("/app/features/children.js", StringComparison.OrdinalIgnoreCase);
            var routerIdx = html.IndexOf("/app/router.js", StringComparison.OrdinalIgnoreCase);

            Assert.True(routerIdx >= 0, "Expected index.html to load /app/router.js");

            if (childrenIdx >= 0)
            {
                Assert.True(childrenIdx < routerIdx, "Expected /app/features/children.js to be loaded before /app/router.js");
            }
        }

        [Fact]
        public void RouterJs_Contains_Children_Route_Renderer()
        {
            var repoRoot = FindRepoRoot();
            var router = ReadText(repoRoot, "src", "Safe0ne.DashboardServer", "wwwroot", "app", "router.js");

            // We accept either modular delegation (ADR-0003 pattern) or an inline renderer.
            // The goal is to catch accidental overwrites that delete Children support entirely.
            bool hasChildrenRoute =
                router.Contains("#/children") ||
                router.Contains("routeChildren") ||
                router.Contains("renderChildren", StringComparison.OrdinalIgnoreCase);

            Assert.True(hasChildrenRoute, "Expected router.js to contain Children route markers (#/children and/or renderChildren).");

            bool delegatesToModule =
                router.Contains("Safe0neChildren.renderChildren", StringComparison.OrdinalIgnoreCase) ||
                router.Contains("window.Safe0neChildren", StringComparison.OrdinalIgnoreCase) ||
                router.Contains("Safe0neChildren.renderChildProfile", StringComparison.OrdinalIgnoreCase);

            bool hasInlineRenderer =
                router.Contains("function renderChildren", StringComparison.OrdinalIgnoreCase) ||
                router.Contains("async function renderChildren", StringComparison.OrdinalIgnoreCase);

            Assert.True(delegatesToModule || hasInlineRenderer,
                "Expected router.js to either delegate Children rendering to Safe0neChildren (ADR-0003) or contain an inline renderChildren() implementation.");
        }
    }
}
