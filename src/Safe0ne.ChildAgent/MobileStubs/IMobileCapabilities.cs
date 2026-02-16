using System.Threading;
using System.Threading.Tasks;

namespace Safe0ne.ChildAgent.MobileStubs;

/// <summary>
/// K11: Mobile-only stubs.
///
/// Windows-first prototype: these are placeholders so we can wire iOS/Android features later
/// without refactoring the whole agent.
/// </summary>
public interface IMobileCapabilities
{
    bool IsLocationAvailable { get; }
    bool IsSosAvailable { get; }
}

public interface ILocationProvider
{
    /// <summary>Returns best-effort current location (or null if unavailable).</summary>
    Task<Safe0ne.Shared.Contracts.GeoCoordinate?> TryGetCurrentAsync(CancellationToken ct);
}

public interface ISosProvider
{
    /// <summary>Raises an SOS signal (best-effort). Returns false if not supported.</summary>
    Task<bool> TryRaiseAsync(string? message, CancellationToken ct);
}
