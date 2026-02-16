using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Safe0ne.Shared.Tests;

/// <summary>
/// Guard test: forbid inline DOM event handlers (onclick/onchange/onsubmit/oninput)
/// inside DashboardServer web UI assets.
///
/// Rationale:
/// - Inline handlers couple behavior to globals and are easy to regress via ZIP overwrites.
/// - The project pattern is data-action + event delegation inside the owning feature module.
/// </summary>
public sealed class InlineDomHandlersGuardTests
{
    [Fact]
    public void DashboardServer_Ui_Has_No_Inline_Event_Handlers()
    {
        var repoRoot = FindRepoRoot();
        var appRoot = Path.Combine(repoRoot, "src", "Safe0ne.DashboardServer", "wwwroot", "app");

        Assert.True(Directory.Exists(appRoot), $"Expected directory not found: {appRoot}");

        // We intentionally scan all text-like UI assets.
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".js",
            ".html",
            ".css",
            ".txt",
            ".md",
        };

        // Match patterns like onclick=, onClick =, onsubmit =, etc.
        var re = new Regex(@"\bon\s*(click|change|submit|input)\s*=\s*[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(appRoot, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file);
            if (!exts.Contains(ext))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch
            {
                // If we can't read the file, treat it as non-text (skip).
                continue;
            }

            var m = re.Match(text);
            if (m.Success)
            {
                var rel = Path.GetRelativePath(repoRoot, file);
                offenders.Add($"{rel} :: {m.Value}");
            }
        }

        if (offenders.Count > 0)
        {
            var message = "Inline DOM handlers found. Replace inline on* handlers with data-action + delegated listeners in the owning feature module.\n" +
                          string.Join("\n", offenders.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            Assert.Fail(message);
        }
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