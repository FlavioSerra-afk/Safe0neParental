using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Safe0ne.ChildAgent.ChildUx;

/// <summary>
/// K7: runs the local child UX HTTP server.
/// </summary>
public sealed class ChildUxHostedService : BackgroundService
{
    private readonly ChildUxServer _server;
    private readonly ILogger<ChildUxHostedService> _logger;

    public ChildUxHostedService(ChildUxServer server, ILogger<ChildUxHostedService> logger)
    {
        _server = server;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _server.Start();
        _logger.LogInformation("Child UX available at {Url}", _server.BaseUrl);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // normal
        }
        finally
        {
            _server.Stop();
        }
    }
}
