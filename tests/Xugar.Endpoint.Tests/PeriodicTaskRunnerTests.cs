using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class PeriodicTaskRunnerTests
{
    [Fact]
    public async Task CancellationStopsAnInProgressLoopCleanly()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var runTask = PeriodicTaskRunner.RunAsync(
            async cancellationToken =>
            {
                Interlocked.Increment(ref calls);
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            TimeSpan.FromMilliseconds(10),
            TimeProvider.System,
            cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SlowOperationsNeverOverlap()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var activeOperations = 0;
        var maximumConcurrentOperations = 0;
        var completedOperations = 0;

        await PeriodicTaskRunner.RunAsync(
            async cancellationToken =>
            {
                var active = Interlocked.Increment(ref activeOperations);
                maximumConcurrentOperations = Math.Max(maximumConcurrentOperations, active);
                try
                {
                    await Task.Delay(20, cancellationToken);
                    if (Interlocked.Increment(ref completedOperations) == 3)
                    {
                        await cancellation.CancelAsync();
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref activeOperations);
                }
            },
            TimeSpan.FromMilliseconds(1),
            TimeProvider.System,
            cancellation.Token);

        Assert.Equal(3, completedOperations);
        Assert.Equal(1, maximumConcurrentOperations);
    }
}
