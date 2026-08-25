using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class ProcessEventDetectorTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 8, 25, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DetectsAStartedProcess()
    {
        var existing = CreateProcess("existing", 10, @"C:\Apps\existing.exe");
        var started = CreateProcess("started", 20, @"C:\Apps\started.exe");

        var events = ProcessEventDetector.Detect([existing], [existing, started], Timestamp);

        var processEvent = Assert.Single(events);
        Assert.Equal(ProcessLifecycleEventType.Start, processEvent.EventType);
        Assert.Equal(20, processEvent.Process.ProcessId);
    }

    [Fact]
    public void DetectsAStoppedProcess()
    {
        var remaining = CreateProcess("remaining", 10, @"C:\Apps\remaining.exe");
        var stopped = CreateProcess("stopped", 20, @"C:\Apps\stopped.exe");

        var events = ProcessEventDetector.Detect([remaining, stopped], [remaining], Timestamp);

        var processEvent = Assert.Single(events);
        Assert.Equal(ProcessLifecycleEventType.Stop, processEvent.EventType);
        Assert.Equal(20, processEvent.Process.ProcessId);
    }

    [Fact]
    public void NewlyAccessiblePathDoesNotCreateFalseTransitionWhenPidAndNameMatch()
    {
        var inaccessible = CreateProcess("work-app", 42, executablePath: null);
        var accessible = CreateProcess("work-app", 42, @"C:\Apps\work-app.exe");

        var events = ProcessEventDetector.Detect([inaccessible], [accessible], Timestamp);

        Assert.Empty(events);
    }

    [Fact]
    public void ReusedPidWithDifferentExecutableProducesStopAndStart()
    {
        var oldProcess = CreateProcess("old-app", 42, @"C:\Apps\old-app.exe");
        var newProcess = CreateProcess("new-app", 42, @"C:\Apps\new-app.exe");

        var events = ProcessEventDetector.Detect([oldProcess], [newProcess], Timestamp);

        Assert.Collection(
            events,
            processEvent => Assert.Equal(ProcessLifecycleEventType.Stop, processEvent.EventType),
            processEvent => Assert.Equal(ProcessLifecycleEventType.Start, processEvent.EventType));
    }

    private static ProcessSnapshotRecord CreateProcess(
        string name,
        int processId,
        string? executablePath) =>
        new(name, processId, executablePath, null, null, null, null);
}
