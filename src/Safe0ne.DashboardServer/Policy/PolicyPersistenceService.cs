namespace Safe0ne.DashboardServer.Policy;

/// <summary>
/// Convenience service: loads cached policy on startup and persists every update.
///
/// You can call UpdatePolicy(...) anywhere you currently apply policy changes.
/// </summary>
public sealed class PolicyPersistenceService
{
    private readonly IPolicyStore _store;
    private readonly PolicyCache _cache;
    private readonly object _lock = new();

    public PolicyPersistenceService(IPolicyStore store, PolicyCache cache)
    {
        _store = store;
        _cache = cache;
    }

    public void LoadIntoCache()
    {
        var loaded = _store.Load();
        if (loaded != null)
            _cache.SetCurrent(loaded);
    }

    public PolicyEnvelope UpdatePolicy<T>(T policy, string updatedBy)
    {
        lock (_lock)
        {
            var current = _cache.GetCurrent();
            var nextVersion = checked(current.Version + 1);

            var envelope = PolicyEnvelope.Create(policy!, nextVersion, updatedBy);
            _store.Save(envelope);
            _cache.SetCurrent(envelope);
            return envelope;
        }
    }
}
