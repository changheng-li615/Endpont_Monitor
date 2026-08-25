namespace Xugar.Endpoint.Service;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Xugar Endpoint Service is a Phase 1 placeholder. It performs no monitoring or desktop capture.");

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
