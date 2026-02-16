namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// Decorator persistence: writes to a primary backend and mirrors the snapshot to a secondary backend.
/// Intended for upgrade/rollback safety (e.g., SQLite primary + JSON file mirror).
/// </summary>
public sealed class MirroringControlPlanePersistence : IControlPlanePersistence
{
    private readonly IControlPlanePersistence _primary;
    private readonly IControlPlanePersistence _mirror;
    private readonly bool _mirrorOnLoad;

    public MirroringControlPlanePersistence(
        IControlPlanePersistence primary,
        IControlPlanePersistence mirror,
        bool mirrorOnLoad = false)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        _mirrorOnLoad = mirrorOnLoad;
    }

    public string? LoadOrNull()
    {
        var json = _primary.LoadOrNull();
        if (_mirrorOnLoad && !string.IsNullOrWhiteSpace(json))
        {
            try { _mirror.Save(json); } catch { }
        }
        return json;
    }

    public void Save(string json)
    {
        _primary.Save(json);
        try { _mirror.Save(json); } catch { }
    }

    public void HealthProbe()
    {
        _primary.HealthProbe();
        try { _mirror.HealthProbe(); } catch { }
    }
}
