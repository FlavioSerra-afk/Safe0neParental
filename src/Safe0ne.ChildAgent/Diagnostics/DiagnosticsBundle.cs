using System.IO.Compression;
using System.Text.Json;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.Diagnostics;

/// <summary>
/// Builds a small privacy-first diagnostics bundle for troubleshooting.
/// Excludes secrets (pairing tokens/auth state).
/// </summary>
public static class DiagnosticsBundle
{
    public static (byte[] ZipBytes, string FileName) BuildZip(ChildId childId, string agentVersion)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"safe0ne_child_{childId.Value}_{stamp}.zip";

        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Safe0ne",
            "ChildAgent");
        Directory.CreateDirectory(baseDir);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Basic agent info
            var info = new
            {
                ChildId = childId.Value,
                DeviceName = Environment.MachineName,
                AgentVersion = agentVersion,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                OsVersion = Environment.OSVersion.VersionString,
                ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()
            };
            WriteJson(zip, "agent_info.json", info);

            // Include local state files (privacy-first) excluding auth/token files.
            // We include only .json and .txt files under the agent dir.
            if (Directory.Exists(baseDir))
            {
                foreach (var path in Directory.EnumerateFiles(baseDir, "*.*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(path);
                    if (name.StartsWith("auth.", StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // contains device token
                    }

                    var ext = Path.GetExtension(name).ToLowerInvariant();
                    if (ext is not ".json" and not ".txt" and not ".log")
                    {
                        continue;
                    }

                    TryAddFile(zip, path, $"state/{name}");
                }
            }
        }

        return (ms.ToArray(), fileName);
    }

    private static void WriteJson(ZipArchive zip, string entryName, object obj)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var s = entry.Open();
        using var writer = new StreamWriter(s);
        var json = JsonSerializer.Serialize(obj, JsonDefaults.Options);
        writer.Write(json);
    }

    private static void TryAddFile(ZipArchive zip, string path, string entryName)
    {
        try
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var es = entry.Open();
            using var fs = File.OpenRead(path);
            fs.CopyTo(es);
        }
        catch
        {
            // best effort
        }
    }
}
