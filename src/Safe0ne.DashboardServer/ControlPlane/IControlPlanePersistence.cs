namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// Persistence abstraction for the Local Control Plane SSOT snapshot.
/// Keeps the JSON schema identical regardless of storage backend.
/// </summary>
public interface IControlPlanePersistence
{
    /// <summary>Returns the persisted SSOT JSON, or null/empty if none exists.</summary>
    string? LoadOrNull();

    /// <summary>Persists the SSOT JSON atomically as best-effort for the backend.</summary>
    void Save(string json);

    /// <summary>Best-effort probe for health checks (writable/readable backend).</summary>
    void HealthProbe();
}
