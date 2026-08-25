using System.IO;
using Microsoft.Extensions.Logging;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Agent.Services;

public sealed class MonitoringCoordinator(
    AgentConfiguration configuration,
    TimeProvider timeProvider,
    IDeviceContextProvider deviceContextProvider,
    IProcessSnapshotProvider processSnapshotProvider,
    IScreenshotCapture screenshotCapture,
    ILocalTelemetryStore telemetryStore,
    RetentionCleanup retentionCleanup,
    ILogger<MonitoringCoordinator> logger) : IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _progressGate = new();
    private readonly string _dataRoot = StoragePaths.ResolveDataRoot(configuration.Storage.RootPath);
    private CancellationTokenSource? _monitoringCancellation;
    private Task? _monitoringTask;
    private DateTimeOffset? _lastScreenshotUtc;
    private DateTimeOffset? _lastProcessSnapshotUtc;
    private bool _disposed;

    public event EventHandler<MonitoringProgress>? ProgressChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_monitoringTask is { IsCompleted: false })
            {
                return;
            }

            Directory.CreateDirectory(_dataRoot);
            await telemetryStore.WriteOperationalEventAsync(
                CreateEvent("monitoring", "Information", "Monitoring started."),
                cancellationToken).ConfigureAwait(false);
            await CleanupAndLogAsync(cancellationToken).ConfigureAwait(false);

            _monitoringCancellation?.Dispose();
            _monitoringCancellation = new CancellationTokenSource();
            _monitoringTask = RunLoopsAsync(_monitoringCancellation.Token);
            Publish(
                isRunning: true,
                status: "Monitoring active",
                detail: "Screenshots and process metadata are being stored locally.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_monitoringTask is null)
            {
                Publish(isRunning: false, status: "Monitoring stopped", detail: "No monitoring loops are running.");
                return;
            }

            _monitoringCancellation?.Cancel();
            await _monitoringTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            _monitoringTask = null;
            _monitoringCancellation?.Dispose();
            _monitoringCancellation = null;

            await TryWriteOperationalEventAsync(
                CreateEvent("monitoring", "Information", "Monitoring stopped."),
                cancellationToken).ConfigureAwait(false);
            Publish(isRunning: false, status: "Monitoring stopped", detail: "No monitoring loops are running.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _monitoringCancellation?.Cancel();
        _monitoringCancellation?.Dispose();
        _lifecycleGate.Dispose();
        _disposed = true;
    }

    private Task RunLoopsAsync(CancellationToken cancellationToken)
    {
        var processLoop = PeriodicTaskRunner.RunAsync(
            CaptureProcessesAsync,
            TimeSpan.FromSeconds(configuration.Monitoring.ProcessIntervalSeconds),
            timeProvider,
            cancellationToken);
        var screenshotLoop = PeriodicTaskRunner.RunAsync(
            CaptureScreenshotsAsync,
            TimeSpan.FromSeconds(configuration.Monitoring.ScreenshotIntervalSeconds),
            timeProvider,
            cancellationToken);

        return Task.WhenAll(processLoop, screenshotLoop);
    }

    private async Task CaptureProcessesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var capturedAtUtc = timeProvider.GetUtcNow();
            var deviceContext = deviceContextProvider.GetCurrent(capturedAtUtc);
            var snapshot = await processSnapshotProvider
                .CaptureAsync(deviceContext, cancellationToken)
                .ConfigureAwait(false);
            await telemetryStore
                .WriteProcessSnapshotAsync(snapshot, cancellationToken)
                .ConfigureAwait(false);

            lock (_progressGate)
            {
                _lastProcessSnapshotUtc = capturedAtUtc;
            }

            await TryWriteOperationalEventAsync(
                CreateEvent(
                    "processSnapshot",
                    "Information",
                    "Process snapshot completed.",
                    new Dictionary<string, object?> { ["processCount"] = snapshot.Processes.Count }),
                cancellationToken).ConfigureAwait(false);
            Publish(
                isRunning: true,
                status: "Monitoring active",
                detail: $"Recorded {snapshot.Processes.Count} running processes.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Process snapshot failed.");
            await TryWriteOperationalEventAsync(
                CreateEvent("processSnapshot", "Warning", "Process snapshot failed."),
                cancellationToken).ConfigureAwait(false);
            Publish(
                isRunning: true,
                status: "Monitoring active with a recent error",
                detail: "The last process snapshot failed; the next scheduled snapshot will still run.");
        }
    }

    private async Task CaptureScreenshotsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var capturedAtUtc = timeProvider.GetUtcNow();
            var screenshots = await screenshotCapture
                .CaptureAsync(_dataRoot, capturedAtUtc, cancellationToken)
                .ConfigureAwait(false);

            if (screenshots.Count == 0)
            {
                await TryWriteOperationalEventAsync(
                    CreateEvent(
                        "screenshot",
                        "Information",
                        "Screenshot skipped because a normal interactive desktop was not available."),
                    cancellationToken).ConfigureAwait(false);
                Publish(
                    isRunning: true,
                    status: "Monitoring active",
                    detail: "Screenshot skipped while the normal interactive desktop was unavailable.");
            }
            else
            {
                await telemetryStore
                    .WriteScreenshotMetadataAsync(screenshots, cancellationToken)
                    .ConfigureAwait(false);

                lock (_progressGate)
                {
                    _lastScreenshotUtc = capturedAtUtc;
                }

                await TryWriteOperationalEventAsync(
                    CreateEvent(
                        "screenshot",
                        "Information",
                        "Screenshot capture completed.",
                        new Dictionary<string, object?> { ["monitorCount"] = screenshots.Count }),
                    cancellationToken).ConfigureAwait(false);
                Publish(
                    isRunning: true,
                    status: "Monitoring active",
                    detail: $"Captured {screenshots.Count} monitor image(s). Data remains local.");
            }

            await CleanupAndLogAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Screenshot capture failed.");
            await TryWriteOperationalEventAsync(
                CreateEvent("screenshot", "Warning", "Screenshot capture failed."),
                cancellationToken).ConfigureAwait(false);
            Publish(
                isRunning: true,
                status: "Monitoring active with a recent error",
                detail: "The last screenshot failed; the next scheduled capture will still run.");
        }
    }

    private async Task CleanupAndLogAsync(CancellationToken cancellationToken)
    {
        var cutoff = RetentionCleanup.CalculateCutoff(
            timeProvider.GetUtcNow(),
            configuration.Monitoring.RetentionHours);
        var result = await retentionCleanup
            .CleanupAsync(_dataRoot, cutoff, cancellationToken)
            .ConfigureAwait(false);

        await TryWriteOperationalEventAsync(
            CreateEvent(
                "retention",
                result.FailedFiles == 0 ? "Information" : "Warning",
                "Retention cleanup completed.",
                new Dictionary<string, object?>
                {
                    ["deletedFiles"] = result.DeletedFiles,
                    ["failedFiles"] = result.FailedFiles,
                    ["skippedReparsePoints"] = result.SkippedReparsePoints
                }),
            cancellationToken).ConfigureAwait(false);
    }

    private OperationalEvent CreateEvent(
        string category,
        string level,
        string message,
        IReadOnlyDictionary<string, object?>? properties = null) =>
        new(timeProvider.GetUtcNow(), category, level, message, properties);

    private async Task TryWriteOperationalEventAsync(
        OperationalEvent operationalEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await telemetryStore
                .WriteOperationalEventAsync(operationalEvent, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not write an operational event to local telemetry.");
        }
    }

    private void Publish(bool isRunning, string status, string detail)
    {
        DateTimeOffset? lastScreenshotUtc;
        DateTimeOffset? lastProcessSnapshotUtc;
        lock (_progressGate)
        {
            lastScreenshotUtc = _lastScreenshotUtc;
            lastProcessSnapshotUtc = _lastProcessSnapshotUtc;
        }

        try
        {
            ProgressChanged?.Invoke(
                this,
                new MonitoringProgress(
                    isRunning,
                    status,
                    detail,
                    lastScreenshotUtc,
                    lastProcessSnapshotUtc));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "A monitoring status subscriber failed.");
        }
    }
}
