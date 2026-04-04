using UIAutomation.Server.Data;

namespace UIAutomation.Server.Services;

/// <summary>
/// Background service that periodically checks for stale test runs
/// (started but never finished) and marks them as timed out.
/// Runs every 5 minutes. Times out runs older than 30 minutes.
/// </summary>
public class RunTimeoutService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RunTimeoutService> _logger;

    public RunTimeoutService(IServiceScopeFactory scopeFactory, ILogger<RunTimeoutService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<SessionService>();
                var count = await service.TimeoutStaleRunsAsync(RunTimeout);

                if (count > 0)
                    _logger.LogInformation("Timed out {Count} stale run(s) older than {Minutes} minutes", count, RunTimeout.TotalMinutes);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for stale runs");
            }
        }
    }
}
