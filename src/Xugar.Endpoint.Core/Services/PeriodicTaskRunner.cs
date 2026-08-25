namespace Xugar.Endpoint.Core.Services;

public static class PeriodicTaskRunner
{
    public static async Task RunAsync(
        Func<CancellationToken, Task> operation,
        TimeSpan interval,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive.");
        }

        try
        {
            await operation(cancellationToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(interval, timeProvider);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await operation(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is the normal way the visible agent stops monitoring.
        }
    }
}
