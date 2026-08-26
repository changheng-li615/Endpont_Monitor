using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Interfaces;

public interface IUploadQueue
{
    Task<UploadQueueEnqueueResult> EnqueueJsonAsync<T>(
        Guid operationId,
        UploadOperationType operationType,
        DateTimeOffset createdAtUtc,
        T payload,
        bool coalesce,
        CancellationToken cancellationToken);

    Task<UploadQueueEnqueueResult> EnqueueFileAsync(
        Guid operationId,
        UploadOperationType operationType,
        DateTimeOffset createdAtUtc,
        string sourcePath,
        string contentType,
        string metadataJson,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UploadQueueItem>> GetReadyAsync(
        DateTimeOffset nowUtc,
        int maximumItems,
        CancellationToken cancellationToken);

    Task MarkRetryAsync(
        Guid operationId,
        int attemptCount,
        DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken);

    Task RemoveAsync(Guid operationId, CancellationToken cancellationToken);

    Task<UploadQueueSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
