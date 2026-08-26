using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class FileUploadQueueTests
{
    [Fact]
    public async Task QueueSurvivesReloadAndCoalescesReplaceableState()
    {
        using var directory = new TemporaryDirectory();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var settings = Settings();
        using (var first = new FileUploadQueue(directory.Path, settings, time))
        {
            await first.EnqueueJsonAsync(
                Guid.NewGuid(), UploadOperationType.Heartbeat, time.UtcNow, new { value = 1 }, true, CancellationToken.None);
            await first.EnqueueJsonAsync(
                Guid.NewGuid(), UploadOperationType.Heartbeat, time.UtcNow, new { value = 2 }, true, CancellationToken.None);
            Assert.Equal(1, (await first.GetSnapshotAsync(CancellationToken.None)).ItemCount);
        }

        using var reloaded = new FileUploadQueue(directory.Path, settings, time);
        var ready = await reloaded.GetReadyAsync(time.UtcNow, 10, CancellationToken.None);
        Assert.Single(ready);
        Assert.Contains("\"value\":2", await File.ReadAllTextAsync(ready[0].PayloadPath));
    }

    [Fact]
    public async Task BoundsEvictOldReplaceableItemsBeforeHistoricalEvents()
    {
        using var directory = new TemporaryDirectory();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var settings = Settings() with { QueueMaxItems = 2 };
        using var queue = new FileUploadQueue(directory.Path, settings, time);
        await queue.EnqueueJsonAsync(Guid.NewGuid(), UploadOperationType.Heartbeat, time.UtcNow, new { a = 1 }, false, CancellationToken.None);
        await queue.EnqueueJsonAsync(Guid.NewGuid(), UploadOperationType.ProcessEvents, time.UtcNow.AddSeconds(1), new { a = 2 }, false, CancellationToken.None);
        var result = await queue.EnqueueJsonAsync(Guid.NewGuid(), UploadOperationType.AgentEvents, time.UtcNow.AddSeconds(2), new { a = 3 }, false, CancellationToken.None);

        var types = (await queue.GetReadyAsync(time.UtcNow.AddMinutes(1), 10, CancellationToken.None))
            .Select(item => item.Envelope.OperationType)
            .ToArray();
        Assert.Contains(UploadOperationType.Heartbeat, result.DroppedOperations);
        Assert.DoesNotContain(UploadOperationType.Heartbeat, types);
        Assert.Contains(UploadOperationType.ProcessEvents, types);
    }

    [Fact]
    public async Task ExpiredAndCorruptEntriesAreHandledDeterministically()
    {
        using var directory = new TemporaryDirectory();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var settings = Settings() with { QueueMaxAgeHours = 1 };
        using var queue = new FileUploadQueue(directory.Path, settings, time);
        await queue.EnqueueJsonAsync(Guid.NewGuid(), UploadOperationType.ProcessEvents, time.UtcNow, new { a = 1 }, false, CancellationToken.None);
        time.UtcNow = time.UtcNow.AddHours(2);
        Assert.Equal(0, (await queue.GetSnapshotAsync(CancellationToken.None)).ItemCount);

        var envelopeDirectory = Path.Combine(StoragePaths.GetUploadQueueDirectory(directory.Path), "envelopes");
        Directory.CreateDirectory(envelopeDirectory);
        await File.WriteAllTextAsync(Path.Combine(envelopeDirectory, "broken.json"), "not-json");
        var snapshot = await queue.GetSnapshotAsync(CancellationToken.None);
        Assert.Equal(1, snapshot.CorruptEntryCount);
        Assert.Empty(Directory.GetFiles(envelopeDirectory));
    }

    [Fact]
    public async Task ExpiredScreenshotIsReportedForAVisibleQueueLimitEvent()
    {
        using var directory = new TemporaryDirectory();
        var screenshot = Path.Combine(directory.Path, "expired.png");
        await File.WriteAllBytesAsync(screenshot, [137, 80, 78, 71, 13, 10, 26, 10]);
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var settings = Settings() with { QueueMaxAgeHours = 1 };
        using var queue = new FileUploadQueue(directory.Path, settings, time);
        await queue.EnqueueFileAsync(
            Guid.NewGuid(), UploadOperationType.Screenshot, time.UtcNow, screenshot, "image/png", "{}", CancellationToken.None);
        time.UtcNow = time.UtcNow.AddHours(2);

        var snapshot = await queue.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(0, snapshot.ItemCount);
        Assert.Equal(1, snapshot.DroppedScreenshotCount);
    }

    [Fact]
    public async Task ScreenshotPayloadMustStayInsideDataRootAndObeysByteLimit()
    {
        using var directory = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var settings = Settings() with { QueueMaxBytes = 128 };
        using var queue = new FileUploadQueue(directory.Path, settings, time);
        var outsideFile = Path.Combine(outside.Path, "outside.png");
        await File.WriteAllBytesAsync(outsideFile, new byte[16]);
        await Assert.ThrowsAsync<ArgumentException>(() => queue.EnqueueFileAsync(
            Guid.NewGuid(), UploadOperationType.Screenshot, time.UtcNow, outsideFile, "image/png", "{}", CancellationToken.None));

        var insideFile = Path.Combine(directory.Path, "large.png");
        await File.WriteAllBytesAsync(insideFile, new byte[256]);
        var result = await queue.EnqueueFileAsync(
            Guid.NewGuid(), UploadOperationType.Screenshot, time.UtcNow, insideFile, "image/png", "{}", CancellationToken.None);
        Assert.False(result.Accepted);
        Assert.Equal(1, result.DroppedScreenshotCount);
    }

    [Fact]
    public async Task RetryMetadataIsAtomicAndCancellationIsHonoured()
    {
        using var directory = new TemporaryDirectory();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var queue = new FileUploadQueue(directory.Path, Settings(), time);
        var id = Guid.NewGuid();
        await queue.EnqueueJsonAsync(id, UploadOperationType.AgentEvents, time.UtcNow, new { a = 1 }, false, CancellationToken.None);
        var next = time.UtcNow.AddMinutes(1);
        await queue.MarkRetryAsync(id, 2, next, CancellationToken.None);
        time.UtcNow = next.AddSeconds(-1);
        Assert.Empty(await queue.GetReadyAsync(time.UtcNow, 1, CancellationToken.None));
        time.UtcNow = next;
        Assert.Equal(2, (await queue.GetReadyAsync(time.UtcNow, 1, CancellationToken.None))[0].Envelope.AttemptCount);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            queue.GetReadyAsync(time.UtcNow, 1, cancellation.Token));
    }

    private static ServerSyncSettings Settings() => new()
    {
        QueueMaxItems = 100,
        QueueMaxBytes = 1024 * 1024,
        QueueMaxAgeHours = 24
    };
}
