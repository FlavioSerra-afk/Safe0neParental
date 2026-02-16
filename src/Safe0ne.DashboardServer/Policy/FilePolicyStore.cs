using System.Text.Json;

namespace Safe0ne.DashboardServer.Policy;

/// <summary>
/// Stores the last-known-good policy snapshot in ProgramData so it survives restarts and
/// is not tied to any particular interactive user profile.
/// </summary>
public sealed class FilePolicyStore : IPolicyStore
{
    private readonly object _lock = new();

    public string PolicyPath { get; }

    public FilePolicyStore(string? baseDir = null)
    {
        // Default to ProgramData\Safe0ne\ControlPlane\policy.json
        var root = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Safe0ne",
            "ControlPlane");

        Directory.CreateDirectory(root);
        PolicyPath = Path.Combine(root, "policy.json");
    }

    public PolicyEnvelope? Load()
    {
        lock (_lock)
        {
            if (!File.Exists(PolicyPath))
                return null;

            var json = File.ReadAllText(PolicyPath);
            return JsonSerializer.Deserialize<PolicyEnvelope>(json, JsonOptions.Default);
        }
    }

    public void Save(PolicyEnvelope envelope)
    {
        if (envelope is null) throw new ArgumentNullException(nameof(envelope));

        lock (_lock)
        {
            var dir = Path.GetDirectoryName(PolicyPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var tmp = PolicyPath + ".tmp";
            var json = JsonSerializer.Serialize(envelope, JsonOptions.Default);

            File.WriteAllText(tmp, json);

            // Atomic replacement on Windows when source/dest are on same volume.
            if (File.Exists(PolicyPath))
            {
                File.Replace(tmp, PolicyPath, null);
            }
            else
            {
                File.Move(tmp, PolicyPath);
            }
        }
    }
}
