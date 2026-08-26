using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class RetryAndQueueProcessorTests
{
    [Fact]
    public void BackoffIsExponentialCappedAndJitterBounded()
    {
        var minimum = TimeSpan.FromSeconds(5);
        var maximum = TimeSpan.FromSeconds(60);
        Assert.Equal(TimeSpan.FromSeconds(4), RetryBackoffCalculator.Calculate(1, minimum, maximum, 0));
        Assert.Equal(TimeSpan.FromSeconds(6), RetryBackoffCalculator.Calculate(1, minimum, maximum, 1));
        Assert.Equal(TimeSpan.FromSeconds(20), RetryBackoffCalculator.Calculate(3, minimum, maximum, 0.5));
        Assert.Equal(maximum, RetryBackoffCalculator.Calculate(20, minimum, maximum, 1));
    }

    [Fact]
    public async Task SuccessfulUploadRemovesEntryWhileRetryableFailureRetainsStablePayload()
    {
        using var directory = new TemporaryDirectory();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var settings = Settings();
        using var queue = new FileUploadQueue(directory.Path, settings, time);
        var operationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var payload = new ProcessEventsRequest([
            new ServerProcessEvent(eventId, time.UtcNow, "START", "notepad", 10, null, null, null, false)
        ]);
        await queue.EnqueueJsonAsync(
            operationId, UploadOperationType.ProcessEvents, time.UtcNow, payload, false, CancellationToken.None);
        var payloadBefore = await File.ReadAllTextAsync(
            (await queue.GetReadyAsync(time.UtcNow, 1, CancellationToken.None))[0].PayloadPath);
        var server = new StubServerClient
        {
            Failure = new XugarServerException(ServerFailureKind.Retryable, "offline")
        };
        var processor = new UploadQueueProcessor(queue, server, settings, time, () => 0.5);

        var retry = await processor.ProcessNextAsync(
            new DeviceCredential(Guid.NewGuid(), "secret"),
            CancellationToken.None);
        Assert.Equal(QueueProcessingOutcome.RetryScheduled, retry.Outcome);
        Assert.Equal(1, (await queue.GetSnapshotAsync(CancellationToken.None)).ItemCount);
        time.UtcNow = retry.NextAttemptAtUtc!.Value;
        var retained = (await queue.GetReadyAsync(time.UtcNow, 1, CancellationToken.None))[0];
        Assert.Equal(payloadBefore, await File.ReadAllTextAsync(retained.PayloadPath));
        Assert.Contains(eventId.ToString("D"), payloadBefore, StringComparison.OrdinalIgnoreCase);

        server.Failure = null;
        var uploaded = await processor.ProcessNextAsync(
            new DeviceCredential(Guid.NewGuid(), "secret"),
            CancellationToken.None);
        Assert.Equal(QueueProcessingOutcome.Uploaded, uploaded.Outcome);
        Assert.Equal(0, (await queue.GetSnapshotAsync(CancellationToken.None)).ItemCount);
    }

    [Fact]
    public async Task AuthenticationFailureRetainsEntryAndReportsDegradedOutcome()
    {
        using var directory = new TemporaryDirectory();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var settings = Settings();
        using var queue = new FileUploadQueue(directory.Path, settings, time);
        await queue.EnqueueJsonAsync(
            Guid.NewGuid(),
            UploadOperationType.Heartbeat,
            time.UtcNow,
            new DeviceHeartbeatRequest(time.UtcNow, "1", "Windows", 1),
            true,
            CancellationToken.None);
        var server = new StubServerClient
        {
            Failure = new XugarServerException(ServerFailureKind.Authentication, "HTTP 401", 401)
        };

        var result = await new UploadQueueProcessor(queue, server, settings, time, () => 0.5)
            .ProcessNextAsync(new DeviceCredential(Guid.NewGuid(), "secret"), CancellationToken.None);

        Assert.Equal(QueueProcessingOutcome.AuthenticationError, result.Outcome);
        Assert.Equal(1, (await queue.GetSnapshotAsync(CancellationToken.None)).ItemCount);
    }

    [Fact]
    public async Task QueuedScreenshotIsRemovedOnlyAfterSuccessfulUpload()
    {
        using var directory = new TemporaryDirectory();
        var screenshotPath = Path.Combine(directory.Path, "approved.png");
        await File.WriteAllBytesAsync(screenshotPath, [137, 80, 78, 71, 13, 10, 26, 10]);
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var settings = Settings();
        using var queue = new FileUploadQueue(directory.Path, settings, time);
        var captureId = Guid.NewGuid();
        var metadata = new ScreenshotQueueMetadata(captureId, time.UtcNow, 1, 100, 50, "image/png");
        await queue.EnqueueFileAsync(
            captureId,
            UploadOperationType.Screenshot,
            time.UtcNow,
            screenshotPath,
            "image/png",
            System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            CancellationToken.None);

        var server = new StubServerClient();
        var result = await new UploadQueueProcessor(queue, server, settings, time)
            .ProcessNextAsync(new DeviceCredential(Guid.NewGuid(), "secret"), CancellationToken.None);

        Assert.Equal(QueueProcessingOutcome.Uploaded, result.Outcome);
        Assert.Equal(1, server.UploadCalls);
        Assert.Equal(0, (await queue.GetSnapshotAsync(CancellationToken.None)).ItemCount);
        Assert.True(File.Exists(screenshotPath));
    }

    [Fact]
    public void ProcessMappingUsesOnlyApprovedFieldsAndStableEventIds()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var device = new DeviceContext(timestamp, "host", "user", "Windows", "1");
        var process = new ProcessSnapshotRecord("app", 42, "C:\\app.exe", null, "1.2", 10 * 1024 * 1024, null);
        var current = SynchronizationPayloadMapper.MapCurrentProcesses(
            new ProcessSnapshot(timestamp, device, [process]));
        var events = SynchronizationPayloadMapper.MapProcessEvents([
            new ProcessLifecycleEvent(timestamp, ProcessLifecycleEventType.Start, process, ProcessCategory.Application)
        ]);

        Assert.Equal(10, current.Processes[0].WorkingSetMb);
        Assert.False(current.Processes[0].IsForeground);
        Assert.NotEqual(Guid.Empty, events[0].ClientEventId);
        Assert.Equal("START", events[0].EventType);
        Assert.DoesNotContain("command", string.Join(',', typeof(ServerProcessRecord).GetProperties().Select(property => property.Name)), StringComparison.OrdinalIgnoreCase);
    }

    private static ServerSyncSettings Settings() => new()
    {
        QueueMaxItems = 100,
        QueueMaxBytes = 1024 * 1024,
        QueueMaxAgeHours = 24,
        RetryMinimumSeconds = 5,
        RetryMaximumSeconds = 60
    };
}
