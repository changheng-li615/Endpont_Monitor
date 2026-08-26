using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Agent.Services;

public sealed class AgentSynchronizationCoordinator(
    AgentConfiguration configuration,
    DeviceEnrollmentService enrollmentService,
    CentralPolicyService policyService,
    IUploadQueue uploadQueue,
    UploadQueueProcessor queueProcessor,
    IDeviceContextProvider deviceContextProvider,
    ILocalTelemetryStore telemetryStore,
    TimeProvider timeProvider,
    ILogger<AgentSynchronizationCoordinator> logger) : IAsyncDisposable, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ServerSyncSettings _settings = configuration.ServerSync;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _enrollmentGate = new(1, 1);
    private readonly SemaphoreSlim _uploadSignal = new(0, 1);
    private readonly object _stateGate = new();
    private CancellationTokenSource? _cancellation;
    private Task? _runTask;
    private DeviceCredential? _credential;
    private IReadOnlyList<ProcessSnapshotRecord>? _previousProcesses;
    private int _enrollmentAttemptCount;
    private DateTimeOffset _nextEnrollmentAttemptUtc = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastQueueLimitEventUtc;
    private SynchronizationProgress _progress = new(
        false,
        "Not enrolled",
        "Disabled",
        null,
        null,
        null,
        0,
        0,
        "Standalone local policy");
    private bool _disposed;

    public event EventHandler<SynchronizationProgress>? ProgressChanged;

    public SynchronizationProgress CurrentProgress
    {
        get
        {
            lock (_stateGate)
            {
                return _progress;
            }
        }
    }

    public PolicyDecision GetPolicyDecision(PolicyActivity activity) =>
        policyService.Evaluate(activity, timeProvider.GetUtcNow());

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_settings.Enabled)
            {
                UpdateProgress(progress => progress with
                {
                    Enabled = false,
                    ServerStatus = "Disabled",
                    PolicyStatus = "Standalone local policy"
                });
                return;
            }
            if (_runTask is { IsCompleted: false })
            {
                return;
            }

            await policyService.LoadCachedAsync(cancellationToken).ConfigureAwait(false);
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            _runTask = RunAsync(_cancellation.Token);
            UpdateProgress(progress => progress with
            {
                Enabled = true,
                ServerStatus = "Connecting",
                PolicyStatus = DescribePolicy()
            });
            await RecordSynchronizationEventAsync(
                "AGENT_STARTED",
                "INFO",
                "The visible Xugar Agent synchronization layer started.",
                cancellationToken).ConfigureAwait(false);
            SignalUpload();
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
            if (_runTask is null)
            {
                return;
            }

            await RecordSynchronizationEventAsync(
                "AGENT_STOPPED",
                "INFO",
                "The visible Xugar Agent synchronization layer stopped.",
                cancellationToken).ConfigureAwait(false);
            _cancellation?.Cancel();
            SignalUpload();
            await _runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            _runTask = null;
            _cancellation?.Dispose();
            _cancellation = null;
            UpdateProgress(progress => progress with { ServerStatus = "Stopped" });
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task OnProcessSnapshotPersistedAsync(
        ProcessSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        IReadOnlyList<ProcessLifecycleEvent> lifecycleEvents = [];
        lock (_stateGate)
        {
            if (_previousProcesses is not null)
            {
                lifecycleEvents = ProcessEventDetector.Detect(
                    _previousProcesses,
                    snapshot.Processes,
                    snapshot.CapturedAtUtc);
            }
            _previousProcesses = snapshot.Processes.ToArray();
        }

        var decision = policyService.Evaluate(PolicyActivity.ProcessMonitoring, snapshot.CapturedAtUtc);
        if (!decision.AllowSynchronization)
        {
            UpdateProgress(progress => progress with { PolicyStatus = DescribePolicy() });
            return;
        }

        var current = SynchronizationPayloadMapper.MapCurrentProcesses(snapshot);
        await EnqueueJsonAsync(
            Guid.NewGuid(),
            UploadOperationType.CurrentProcesses,
            snapshot.CapturedAtUtc,
            current,
            coalesce: true,
            cancellationToken).ConfigureAwait(false);

        var events = SynchronizationPayloadMapper.MapProcessEvents(lifecycleEvents);
        foreach (var batch in events.Chunk(_settings.UploadBatchSize))
        {
            await EnqueueJsonAsync(
                Guid.NewGuid(),
                UploadOperationType.ProcessEvents,
                snapshot.CapturedAtUtc,
                new ProcessEventsRequest(batch),
                coalesce: false,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task OnScreenshotsPersistedAsync(
        IReadOnlyList<ScreenshotMetadata> screenshots,
        CancellationToken cancellationToken)
    {
        if (!_settings.Enabled || screenshots.Count == 0)
        {
            return;
        }

        var decision = policyService.Evaluate(
            PolicyActivity.ScreenshotCapture,
            screenshots[0].CapturedAtUtc);
        if (!decision.AllowSynchronization)
        {
            return;
        }

        foreach (var screenshot in screenshots)
        {
            var captureId = Guid.NewGuid();
            var mimeType = Path.GetExtension(screenshot.FilePath).Equals(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png"
                : "image/jpeg";
            var metadata = new ScreenshotQueueMetadata(
                captureId,
                screenshot.CapturedAtUtc,
                screenshot.MonitorIndex,
                screenshot.PixelWidth,
                screenshot.PixelHeight,
                mimeType);
            var result = await uploadQueue.EnqueueFileAsync(
                captureId,
                UploadOperationType.Screenshot,
                screenshot.CapturedAtUtc,
                screenshot.FilePath,
                mimeType,
                JsonSerializer.Serialize(metadata, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            await HandleQueueResultAsync(result, cancellationToken).ConfigureAwait(false);
        }

        SignalUpload();
        await RefreshQueueStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task OnOperationalEventPersistedAsync(
        OperationalEvent operationalEvent,
        CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        if (NormalizeSeverity(operationalEvent.Level) == "INFO" &&
            !operationalEvent.Category.Equals("monitoring", StringComparison.OrdinalIgnoreCase) &&
            !operationalEvent.Message.Contains("skipped", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var serverEvent = new ServerAgentEvent(
            Guid.NewGuid(),
            operationalEvent.TimestampUtc,
            MapOperationalEventType(operationalEvent),
            NormalizeSeverity(operationalEvent.Level),
            Truncate(operationalEvent.Message, 1_000));
        await EnqueueJsonAsync(
            Guid.NewGuid(),
            UploadOperationType.AgentEvents,
            operationalEvent.TimestampUtc,
            new AgentEventsRequest([serverEvent]),
            coalesce: false,
            cancellationToken).ConfigureAwait(false);
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

        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _lifecycleGate.Dispose();
        _enrollmentGate.Dispose();
        _uploadSignal.Dispose();
        _disposed = true;
    }

    private Task RunAsync(CancellationToken cancellationToken) => Task.WhenAll(
        RunHeartbeatLoopAsync(cancellationToken),
        RunPolicyLoopAsync(cancellationToken),
        RunUploadLoopAsync(cancellationToken));

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var now = timeProvider.GetUtcNow();
                var device = deviceContextProvider.GetCurrent(now);
                await EnqueueJsonAsync(
                    Guid.NewGuid(),
                    UploadOperationType.Heartbeat,
                    now,
                    new DeviceHeartbeatRequest(
                        now,
                        device.ApplicationVersion,
                        device.OperatingSystem,
                        Math.Max(0, Environment.TickCount64 / 1_000)),
                    coalesce: true,
                    cancellationToken).ConfigureAwait(false);
                await Task.Delay(
                    TimeSpan.FromSeconds(_settings.HeartbeatIntervalSeconds),
                    timeProvider,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunPolicyLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                try
                {
                    var credential = await GetCredentialAsync(cancellationToken).ConfigureAwait(false);
                    var previousVersion = GetPolicyDecision(PolicyActivity.ScreenshotCapture).PolicyVersion;
                    var policy = await policyService.RefreshAsync(credential, cancellationToken).ConfigureAwait(false);
                    UpdateProgress(progress => progress with
                    {
                        EnrollmentStatus = "Enrolled",
                        ServerStatus = "Connected",
                        LastPolicyRefreshUtc = policyService.LastRefreshUtc,
                        PolicyStatus = DescribePolicy()
                    });
                    if (previousVersion != policy.Version)
                    {
                        await RecordSynchronizationEventAsync(
                            "POLICY_UPDATED",
                            "INFO",
                            $"Central monitoring policy version {policy.Version} was cached.",
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (XugarServerException exception)
                {
                    SetServerFailure(exception);
                    await RecordPolicyUnavailableAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    UpdateProgress(progress => progress with
                    {
                        EnrollmentStatus = "Not enrolled",
                        ServerStatus = "Configuration error",
                        PolicyStatus = DescribePolicy()
                    });
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(_settings.PolicyRefreshIntervalSeconds),
                    timeProvider,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunUploadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                try
                {
                    var credential = await GetCredentialAsync(cancellationToken).ConfigureAwait(false);
                    var result = await queueProcessor.ProcessNextAsync(credential, cancellationToken)
                        .ConfigureAwait(false);
                    switch (result.Outcome)
                    {
                        case QueueProcessingOutcome.Uploaded:
                            var now = timeProvider.GetUtcNow();
                            UpdateProgress(progress => progress with
                            {
                                EnrollmentStatus = "Enrolled",
                                ServerStatus = "Connected",
                                LastSuccessfulUploadUtc = now,
                                LastHeartbeatUtc = result.OperationType == UploadOperationType.Heartbeat
                                    ? now
                                    : progress.LastHeartbeatUtc
                            });
                            break;
                        case QueueProcessingOutcome.AuthenticationError:
                            UpdateProgress(progress => progress with
                            {
                                EnrollmentStatus = "Credential rejected",
                                ServerStatus = "Authentication error"
                            });
                            break;
                        case QueueProcessingOutcome.RetryScheduled:
                            UpdateProgress(progress => progress with { ServerStatus = "Offline / retrying" });
                            break;
                        case QueueProcessingOutcome.DiscardedInvalid:
                            await RecordLocalOnlyEventAsync(
                                "UPLOAD_DISCARDED",
                                "WARNING",
                                "A malformed or non-retryable queued upload was discarded.",
                                cancellationToken).ConfigureAwait(false);
                            break;
                    }

                    await RefreshQueueStatusAsync(cancellationToken).ConfigureAwait(false);
                    if (result.Outcome == QueueProcessingOutcome.Uploaded)
                    {
                        continue;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (XugarServerException exception)
                {
                    SetServerFailure(exception);
                }
                catch (InvalidOperationException)
                {
                    UpdateProgress(progress => progress with
                    {
                        EnrollmentStatus = "Not enrolled",
                        ServerStatus = "Configuration error"
                    });
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Synchronization queue processing failed safely.");
                    UpdateProgress(progress => progress with { ServerStatus = "Offline / retrying" });
                }

                await _uploadSignal.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<DeviceCredential> GetCredentialAsync(CancellationToken cancellationToken)
    {
        if (_credential is not null)
        {
            return _credential;
        }

        await _enrollmentGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_credential is not null)
            {
                return _credential;
            }
            var now = timeProvider.GetUtcNow();
            if (now < _nextEnrollmentAttemptUtc)
            {
                throw new XugarServerException(
                    ServerFailureKind.Retryable,
                    "Enrollment retry is delayed by backoff.");
            }

            try
            {
                _credential = await enrollmentService.EnsureEnrolledAsync(cancellationToken)
                    .ConfigureAwait(false);
                _enrollmentAttemptCount = 0;
                _nextEnrollmentAttemptUtc = DateTimeOffset.MinValue;
                UpdateProgress(progress => progress with { EnrollmentStatus = "Enrolled" });
                await RecordSynchronizationEventAsync(
                    "ENROLLMENT_SUCCEEDED",
                    "INFO",
                    "This installation has a protected Xugar device credential.",
                    cancellationToken).ConfigureAwait(false);
                return _credential;
            }
            catch (Exception exception) when (exception is XugarServerException or HttpRequestException)
            {
                _enrollmentAttemptCount++;
                var delay = RetryBackoffCalculator.Calculate(
                    _enrollmentAttemptCount,
                    TimeSpan.FromSeconds(_settings.RetryMinimumSeconds),
                    TimeSpan.FromSeconds(_settings.RetryMaximumSeconds),
                    0.5);
                _nextEnrollmentAttemptUtc = now + delay;
                throw;
            }
        }
        finally
        {
            _enrollmentGate.Release();
        }
    }

    private async Task EnqueueJsonAsync<T>(
        Guid operationId,
        UploadOperationType operationType,
        DateTimeOffset createdAtUtc,
        T payload,
        bool coalesce,
        CancellationToken cancellationToken)
    {
        var result = await uploadQueue.EnqueueJsonAsync(
            operationId,
            operationType,
            createdAtUtc,
            payload,
            coalesce,
            cancellationToken).ConfigureAwait(false);
        await HandleQueueResultAsync(result, cancellationToken).ConfigureAwait(false);
        SignalUpload();
        await RefreshQueueStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleQueueResultAsync(
        UploadQueueEnqueueResult result,
        CancellationToken cancellationToken)
    {
        if (result.Accepted && result.DroppedScreenshotCount == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (_lastQueueLimitEventUtc is not null && now - _lastQueueLimitEventUtc < TimeSpan.FromMinutes(5))
        {
            return;
        }

        _lastQueueLimitEventUtc = now;
        await RecordLocalOnlyEventAsync(
            "UPLOAD_QUEUE_LIMIT_REACHED",
            "WARNING",
            result.DroppedScreenshotCount > 0
                ? "The bounded upload queue discarded an eligible screenshot while preserving local Phase 1 data."
                : "The bounded upload queue discarded telemetry according to its deterministic limits.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordPolicyUnavailableAsync(CancellationToken cancellationToken)
    {
        await RecordLocalOnlyEventAsync(
            "POLICY_UNAVAILABLE",
            "WARNING",
            "Central policy is unavailable; new unrestricted screenshots remain denied.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordSynchronizationEventAsync(
        string eventType,
        string severity,
        string message,
        CancellationToken cancellationToken)
    {
        await RecordLocalOnlyEventAsync(eventType, severity, message, cancellationToken)
            .ConfigureAwait(false);
        var serverEvent = new ServerAgentEvent(Guid.NewGuid(), timeProvider.GetUtcNow(), eventType, severity, message);
        await EnqueueJsonAsync(
            Guid.NewGuid(),
            UploadOperationType.AgentEvents,
            serverEvent.OccurredAt,
            new AgentEventsRequest([serverEvent]),
            coalesce: false,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordLocalOnlyEventAsync(
        string eventType,
        string severity,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await telemetryStore.WriteOperationalEventAsync(
                new OperationalEvent(
                    timeProvider.GetUtcNow(),
                    eventType,
                    severity,
                    message),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "A synchronization health event could not be written locally.");
        }
    }

    private async Task RefreshQueueStatusAsync(CancellationToken cancellationToken)
    {
        var snapshot = await uploadQueue.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.DroppedScreenshotCount > 0)
        {
            await HandleQueueResultAsync(
                new UploadQueueEnqueueResult(
                    true,
                    Enumerable.Repeat(
                        UploadOperationType.Screenshot,
                        snapshot.DroppedScreenshotCount).ToArray()),
                cancellationToken).ConfigureAwait(false);
        }
        UpdateProgress(progress => progress with
        {
            PendingQueueItems = snapshot.ItemCount,
            PendingQueueBytes = snapshot.TotalBytes,
            PolicyStatus = DescribePolicy()
        });
    }

    private void SetServerFailure(XugarServerException exception)
    {
        UpdateProgress(progress => progress with
        {
            EnrollmentStatus = exception.Kind == ServerFailureKind.Authentication
                ? "Credential rejected"
                : progress.EnrollmentStatus,
            ServerStatus = exception.Kind == ServerFailureKind.Authentication
                ? "Authentication error"
                : "Offline / retrying",
            PolicyStatus = DescribePolicy()
        });
    }

    private string DescribePolicy()
    {
        var screenshot = policyService.Evaluate(PolicyActivity.ScreenshotCapture, timeProvider.GetUtcNow());
        return screenshot.Reason switch
        {
            PolicyDecisionReason.Allowed => $"Version {screenshot.PolicyVersion}: inside approved schedule",
            PolicyDecisionReason.SynchronizationDisabled => "Standalone local policy",
            PolicyDecisionReason.OutsideSchedule => $"Version {screenshot.PolicyVersion}: outside approved schedule",
            PolicyDecisionReason.MonitoringDisabled => $"Version {screenshot.PolicyVersion}: monitoring disabled",
            PolicyDecisionReason.ActivityDisabled => $"Version {screenshot.PolicyVersion}: screenshots disabled",
            PolicyDecisionReason.Expired => "Cached policy expired; screenshots denied",
            PolicyDecisionReason.Invalid => "Invalid policy; screenshots denied",
            _ => "Policy unavailable; screenshots denied"
        };
    }

    private void UpdateProgress(Func<SynchronizationProgress, SynchronizationProgress> update)
    {
        SynchronizationProgress next;
        lock (_stateGate)
        {
            next = update(_progress);
            _progress = next;
        }

        try
        {
            ProgressChanged?.Invoke(this, next);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "A synchronization status subscriber failed.");
        }
    }

    private void SignalUpload()
    {
        try
        {
            if (_uploadSignal.CurrentCount == 0)
            {
                _uploadSignal.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Another producer signalled between the count check and release.
        }
    }

    private static string NormalizeEventType(string value)
    {
        var normalized = new string(value
            .Select(character => char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_')
            .ToArray());
        return Truncate(string.IsNullOrWhiteSpace(normalized) ? "AGENT_EVENT" : normalized, 64);
    }

    private static string MapOperationalEventType(OperationalEvent operationalEvent)
    {
        if (operationalEvent.Category.Equals("screenshot", StringComparison.OrdinalIgnoreCase) &&
            operationalEvent.Message.Contains("interactive desktop", StringComparison.OrdinalIgnoreCase))
        {
            return "SCREENSHOT_SKIPPED_LOCKED_DESKTOP";
        }

        return NormalizeEventType(operationalEvent.Category);
    }

    private static string NormalizeSeverity(string value) => value.ToUpperInvariant() switch
    {
        "ERROR" => "ERROR",
        "WARNING" => "WARNING",
        _ => "INFO"
    };

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
