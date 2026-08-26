namespace Xugar.Endpoint.Core.Models;

public enum UploadOperationType
{
    Heartbeat,
    CurrentProcesses,
    ProcessEvents,
    Screenshot,
    AgentEvents
}

public sealed record UploadQueueEnvelope(
    int Version,
    Guid OperationId,
    UploadOperationType OperationType,
    DateTimeOffset CreatedAtUtc,
    int AttemptCount,
    DateTimeOffset NextAttemptAtUtc,
    string PayloadRelativePath,
    string ContentType,
    string? MetadataJson);

public sealed record UploadQueueItem(UploadQueueEnvelope Envelope, string PayloadPath);

public sealed record UploadQueueSnapshot(
    int ItemCount,
    long TotalBytes,
    int CorruptEntryCount,
    int DroppedScreenshotCount);

public sealed record UploadQueueEnqueueResult(
    bool Accepted,
    IReadOnlyList<UploadOperationType> DroppedOperations)
{
    public int DroppedScreenshotCount =>
        DroppedOperations.Count(type => type == UploadOperationType.Screenshot);
}
