
using System.Text;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.WebFilter;

internal sealed class HostsFileManager
{
    private const string BeginMarker = "# SAFE0NE BEGIN";
    private const string EndMarker = "# SAFE0NE END";
    private const string RedirectIp = "127.0.0.1";

    public bool TryApply(HashSet<string> blockedDomains, out string? error)
    {
        error = null;
        try
        {
            var path = GetHostsPath();
            var lines = File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8).ToList() : new List<string>();

            // Remove existing SAFE0NE section
            var beginIdx = lines.FindIndex(l => l.Trim().Equals(BeginMarker, StringComparison.OrdinalIgnoreCase));
            if (beginIdx >= 0)
            {
                var endIdx = lines.FindIndex(beginIdx + 1, l => l.Trim().Equals(EndMarker, StringComparison.OrdinalIgnoreCase));
                if (endIdx >= 0)
                {
                    lines.RemoveRange(beginIdx, endIdx - beginIdx + 1);
                }
                else
                {
                    // marker mismatch: remove from begin to end of file
                    lines.RemoveRange(beginIdx, lines.Count - beginIdx);
                }
            }

            // Insert new section at end
            lines.Add(BeginMarker);
            lines.Add("# Web blocks applied by Safe0ne (Windows-first prototype).");
            foreach (var d in blockedDomains.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"{RedirectIp} {d}");
                lines.Add($"{RedirectIp} www.{d}");
            }
            lines.Add(EndMarker);

            File.WriteAllLines(path, lines, Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string GetHostsPath()
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
        // ...\System32
        var system32 = systemRoot;
        return Path.Combine(system32, "drivers", "etc", "hosts");
    }
}
