using System.Threading;
using System.Threading.Tasks;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.MobileStubs;

/// <summary>
/// Windows implementation for K11 stubs.
///
/// Location/SOS are not supported in the Windows-first prototype.
/// </summary>
public sealed class WindowsMobileCapabilities : IMobileCapabilities, ILocationProvider, ISosProvider
{
    public bool IsLocationAvailable => false;
    public bool IsSosAvailable => false;

    public Task<GeoCoordinate?> TryGetCurrentAsync(CancellationToken ct)
        => Task.FromResult<GeoCoordinate?>(null);

    public Task<bool> TryRaiseAsync(string? message, CancellationToken ct)
        => Task.FromResult(false);
}
