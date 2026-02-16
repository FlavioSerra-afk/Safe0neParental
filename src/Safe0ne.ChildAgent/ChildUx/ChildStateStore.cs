using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.ChildUx;

/// <summary>
/// Thread-safe snapshot of what the agent is currently enforcing.
/// K7 consumes this to render the local child-facing UI.
/// </summary>
public sealed class ChildStateStore
{
    private readonly object _lock = new();

    private EffectiveChildState? _effective;
    private ChildPolicy? _policy;
    private ScreenTimeSnapshot _screenTime = new(TimeSpan.Zero, TimeSpan.Zero, false);

    public void Update(EffectiveChildState? effective, ChildPolicy? policy, ScreenTimeSnapshot screenTime)
    {
        lock (_lock)
        {
            _effective = effective;
            _policy = policy;
            _screenTime = screenTime;
        }
    }

    public (EffectiveChildState? Effective, ChildPolicy? Policy, ScreenTimeSnapshot ScreenTime) GetSnapshot()
    {
        lock (_lock)
        {
            return (_effective, _policy, _screenTime);
        }
    }
}

public sealed record ScreenTimeSnapshot(
    TimeSpan UsedToday,
    TimeSpan RemainingToday,
    bool LimitEnabled);
