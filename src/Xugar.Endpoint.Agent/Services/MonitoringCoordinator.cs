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
    IProcessReportWriter processReportWriter,
    AgentSynchronizationCoordinator synchronizationCoordinator,
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
    private PolicyDecisionReason? _lastProcessSkipReason;
    private PolicyDecisionReason? _lastScreenshotSkipReason;

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
            await TryStartSynchronizationAsync(cancellationToken).ConfigureAwait(false);

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
            await TryStopSynchronizationAsync(cancellationToken).ConfigureAwait(false);
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
        var processLoop = RunPolicyAwareLoopAsync(
            PolicyActivity.ProcessMonitoring,
            CaptureProcessesAsync,
            cancellationToken);
        var screenshotLoop = RunPolicyAwareLoopAsync(
            PolicyActivity.ScreenshotCapture,
            CaptureScreenshotsAsync,
            cancellationToken);

        return Task.WhenAll(processLoop, screenshotLoop);
    }

    private async Task RunPolicyAwareLoopAsync(
        PolicyActivity activity,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var decision = synchronizationCoordinator.GetPolicyDecision(activity);
                if (decision.AllowLocalCapture)
                {
                    ClearSkipReason(activity);
                    await operation(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WritePolicySkipOnceAsync(activity, decision, cancellationToken)
                        .ConfigureAwait(false);
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(decision.IntervalSeconds),
                    timeProvider,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
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
            await TryWriteProcessReportsAsync(snapshot, cancellationToken).ConfigureAwait(false);
            await TrySynchronizeProcessSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);

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
                await TrySynchronizeScreenshotsAsync(screenshots, cancellationToken).ConfigureAwait(false);

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

    private async Task TryWriteProcessReportsAsync(
        ProcessSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            await processReportWriter
                .WriteSnapshotAsync(snapshot, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Derived process CSV reporting failed.");
            await TryWriteOperationalEventAsync(
                CreateEvent(
                    "processReport",
                    "Warning",
                    "Derived process CSV reporting failed; canonical JSONL capture succeeded."),
                cancellationToken).ConfigureAwait(false);
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
            return;
        }

        try
        {
            await synchronizationCoordinator
                .OnOperationalEventPersistedAsync(operationalEvent, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Operational event synchronization staging failed after local persistence succeeded.");
        }
    }

    private async Task TryStartSynchronizationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await synchronizationCoordinator.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Server synchronization could not start; local monitoring will continue safely.");
        }
    }

    private async Task TryStopSynchronizationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await synchronizationCoordinator.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Server synchronization could not stop cleanly; the persistent queue remains on disk.");
        }
    }

    private async Task TrySynchronizeProcessSnapshotAsync(
        ProcessSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            await synchronizationCoordinator.OnProcessSnapshotPersistedAsync(snapshot, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Process synchronization staging failed after local persistence succeeded.");
        }
    }

    private async Task TrySynchronizeScreenshotsAsync(
        IReadOnlyList<ScreenshotMetadata> screenshots,
        CancellationToken cancellationToken)
    {
        try
        {
            await synchronizationCoordinator.OnScreenshotsPersistedAsync(screenshots, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Screenshot synchronization staging failed after local persistence succeeded.");
        }
    }

    private async Task WritePolicySkipOnceAsync(
        PolicyActivity activity,
        PolicyDecision decision,
        CancellationToken cancellationToken)
    {
        var previous = activity == PolicyActivity.ScreenshotCapture
            ? _lastScreenshotSkipReason
            : _lastProcessSkipReason;
        if (previous == decision.Reason)
        {
            return;
        }

        if (activity == PolicyActivity.ScreenshotCapture)
        {
            _lastScreenshotSkipReason = decision.Reason;
        }
        else
        {
            _lastProcessSkipReason = decision.Reason;
        }

        var category = activity == PolicyActivity.ScreenshotCapture
            ? "SCREENSHOT_SKIPPED_POLICY"
            : "PROCESS_SNAPSHOT_SKIPPED_POLICY";
        await TryWriteOperationalEventAsync(
            CreateEvent(
                category,
                "Information",
                $"{activity} skipped because central policy state is {decision.Reason}."),
            cancellationToken).ConfigureAwait(false);
        Publish(
            isRunning: true,
            status: "Monitoring active with central policy",
            detail: $"{activity} is currently paused: {decision.Reason}.");
    }

    private void ClearSkipReason(PolicyActivity activity)
    {
        if (activity == PolicyActivity.ScreenshotCapture)
        {
            _lastScreenshotSkipReason = null;
        }
        else
        {
            _lastProcessSkipReason = null;
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
