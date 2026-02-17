using Microsoft.Extensions.Hosting;
using Safe0ne.DashboardServer.ControlPlane;

namespace Safe0ne.DashboardServer.Devices;

/// <summary>
/// 16W12: Background sweep for time-based device health transitions.
/// - Online transitions are emitted on authenticated heartbeats.
/// - Offline transitions are detected by this sweep when LastSeenUtc becomes stale.
/// </summary>
public sealed class DeviceHealthSweepService : BackgroundService
{
    private readonly JsonFileControlPlane _cp;

    public DeviceHealthSweepService(JsonFileControlPlane cp)
    {
        _cp = cp;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Keep the cadence modest; we only need to catch offline transitions.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                _cp.SweepDeviceHealth(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(3));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // best-effort; never crash the server
            }
        }
    }
}
