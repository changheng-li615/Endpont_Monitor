namespace Xugar.Endpoint.Core.Models;

public sealed record ScreenshotQueueMetadata(
    Guid CaptureId,
    DateTimeOffset CapturedAt,
    int MonitorIndex,
    int Width,
    int Height,
    string MimeType);

public enum QueueProcessingOutcome
{
    NoneReady,
    Uploaded,
    RetryScheduled,
    AuthenticationError,
    DiscardedInvalid
}

public sealed record QueueProcessingResult(
    QueueProcessingOutcome Outcome,
    UploadOperationType? OperationType,
    DateTimeOffset? NextAttemptAtUtc = null);
