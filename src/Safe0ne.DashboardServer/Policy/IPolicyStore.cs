namespace Safe0ne.DashboardServer.Policy;

public interface IPolicyStore
{
    /// <summary>Loads the last-known-good policy snapshot (or null if none exists).</summary>
    PolicyEnvelope? Load();

    /// <summary>Persists the given policy snapshot as the last-known-good policy.</summary>
    void Save(PolicyEnvelope envelope);

    /// <summary>The resolved absolute path where the policy snapshot is stored.</summary>
    string PolicyPath { get; }
}
