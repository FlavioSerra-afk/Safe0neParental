using Microsoft.Extensions.Hosting;

namespace Safe0ne.DashboardServer.Policy;

public sealed class PolicyWarmupHostedService : IHostedService
{
    private readonly PolicyPersistenceService _persistence;

    public PolicyWarmupHostedService(PolicyPersistenceService persistence)
    {
        _persistence = persistence;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _persistence.LoadIntoCache();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
