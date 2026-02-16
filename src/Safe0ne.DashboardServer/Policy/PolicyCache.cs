using System.Text.Json;

namespace Safe0ne.DashboardServer.Policy;

/// <summary>
/// Thread-safe in-memory cache for the current policy envelope.
///
/// Integrate this with your existing policy engine/service by:
///  - calling <see cref="SetCurrent"/> whenever policy changes
///  - reading <see cref="GetCurrent"/> when serving the Parent App
/// </summary>
public sealed class PolicyCache
{
    private readonly ReaderWriterLockSlim _rw = new();
    private PolicyEnvelope _current;

    public PolicyCache()
    {
        // Empty default to avoid null checks.
        _current = new PolicyEnvelope
        {
            Version = 0,
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            UpdatedBy = "system",
            Policy = JsonDocument.Parse("{}").RootElement
        };
    }

    public PolicyEnvelope GetCurrent()
    {
        _rw.EnterReadLock();
        try { return _current; }
        finally { _rw.ExitReadLock(); }
    }

    public void SetCurrent(PolicyEnvelope envelope)
    {
        if (envelope is null) throw new ArgumentNullException(nameof(envelope));

        _rw.EnterWriteLock();
        try { _current = envelope; }
        finally { _rw.ExitWriteLock(); }
    }
}
