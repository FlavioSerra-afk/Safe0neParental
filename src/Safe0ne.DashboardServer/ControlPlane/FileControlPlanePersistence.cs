using System.Text;

namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// Default persistence: a single JSON file under LocalApplicationData.
/// </summary>
public sealed class FileControlPlanePersistence : IControlPlanePersistence
{
    private readonly string _path;

    public FileControlPlanePersistence(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            _path = overridePath;
            var dir0 = Path.GetDirectoryName(_path) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(dir0)) Directory.CreateDirectory(dir0);
            return;
        }

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Safe0ne",
            "DashboardServer");

        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "control-plane.v1.json");
    }

    public string? LoadOrNull()
    {
        if (!File.Exists(_path)) return null;
        return File.ReadAllText(_path, Encoding.UTF8);
    }

    public void Save(string json)
    {
        // Safer replace: write temp then atomically swap into place on Windows.
        // If the destination doesn't exist yet, fall back to move.
        var tmp = _path + ".tmp";
        var bak = _path + ".bak";
        File.WriteAllText(tmp, json, Encoding.UTF8);
        if (File.Exists(_path))
        {
            File.Replace(tmp, _path, bak, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, _path);
        }
        if (File.Exists(tmp)) File.Delete(tmp);
        if (File.Exists(bak))
        {
            try { File.Delete(bak); } catch { /* ignore */ }
        }
    }

    public void HealthProbe()
    {
        var dir = Path.GetDirectoryName(_path) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var probe = Path.Combine(dir, "health.probe");
        File.WriteAllText(probe, DateTimeOffset.UtcNow.ToString("O"), Encoding.UTF8);
        File.Delete(probe);
    }
}
