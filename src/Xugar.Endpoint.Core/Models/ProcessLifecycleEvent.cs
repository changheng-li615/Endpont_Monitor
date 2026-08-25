namespace Xugar.Endpoint.Core.Models;

public enum ProcessLifecycleEventType
{
    Start,
    Stop
}

public sealed record ProcessLifecycleEvent(
    DateTimeOffset TimestampUtc,
    ProcessLifecycleEventType EventType,
    ProcessSnapshotRecord Process,
    ProcessCategory Category);
