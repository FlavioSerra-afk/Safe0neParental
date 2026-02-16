using System.Collections.Concurrent;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// Temporary in-memory control plane for the walking skeleton.
/// P2/P3 will replace this with persistent storage.
/// </summary>
public sealed class InMemoryControlPlane
{
    private readonly List<ChildProfile> _children;
    private readonly ConcurrentDictionary<ChildId, ChildPolicy> _policies = new();

    public InMemoryControlPlane()
    {
        // Deterministic seed so UI and integration tests can target a known child.
        var childId = new ChildId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        _children = new List<ChildProfile>
        {
            new(childId, "Demo Child")
        };

        _policies[childId] = new ChildPolicy(
            ChildId: childId,
            Version: PolicyVersion.Initial,
            Mode: SafetyMode.Open,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedBy: "system");
    }

    public IReadOnlyList<ChildProfile> GetChildren() => _children;

    public bool TryGetPolicy(ChildId childId, out ChildPolicy policy) => _policies.TryGetValue(childId, out policy!);

    public ChildPolicy UpsertPolicy(ChildId childId, SafetyMode mode, string updatedBy)
    {
        return _policies.AddOrUpdate(
            childId,
            _ => new ChildPolicy(childId, PolicyVersion.Initial, mode, DateTimeOffset.UtcNow, updatedBy),
            (_, existing) => existing with
            {
                Version = existing.Version.Next(),
                Mode = mode,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedBy = updatedBy
            });
    }
}
