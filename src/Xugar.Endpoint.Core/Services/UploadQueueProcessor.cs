using System.Text.Json;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public sealed class UploadQueueProcessor(
    IUploadQueue queue,
    IXugarServerClient serverClient,
    ServerSyncSettings settings,
    TimeProvider timeProvider,
    Func<double>? jitterSource = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Func<double> _jitterSource = jitterSource ?? Random.Shared.NextDouble;

    public async Task<QueueProcessingResult> ProcessNextAsync(
        DeviceCredential credential,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var ready = await queue.GetReadyAsync(now, 1, cancellationToken).ConfigureAwait(false);
        var item = ready.FirstOrDefault();
        if (item is null)
        {
            return new QueueProcessingResult(QueueProcessingOutcome.NoneReady, null);
        }

        try
        {
            await DispatchAsync(item, credential, cancellationToken).ConfigureAwait(false);
            await queue.RemoveAsync(item.Envelope.OperationId, cancellationToken).ConfigureAwait(false);
            return new QueueProcessingResult(
                QueueProcessingOutcome.Uploaded,
                item.Envelope.OperationType);
        }
        catch (XugarServerException exception) when (exception.Kind == ServerFailureKind.Authentication)
        {
            var next = await ScheduleRetryAsync(item, cancellationToken).ConfigureAwait(false);
            return new QueueProcessingResult(
                QueueProcessingOutcome.AuthenticationError,
                item.Envelope.OperationType,
                next);
        }
        catch (XugarServerException exception) when (exception.Kind == ServerFailureKind.Retryable)
        {
            var next = await ScheduleRetryAsync(item, cancellationToken).ConfigureAwait(false);
            return new QueueProcessingResult(
                QueueProcessingOutcome.RetryScheduled,
                item.Envelope.OperationType,
                next);
        }
        catch (Exception exception) when (
            exception is XugarServerException or JsonException or IOException or InvalidDataException)
        {
            await queue.RemoveAsync(item.Envelope.OperationId, cancellationToken).ConfigureAwait(false);
            return new QueueProcessingResult(
                QueueProcessingOutcome.DiscardedInvalid,
                item.Envelope.OperationType);
        }
    }

    private async Task DispatchAsync(
        UploadQueueItem item,
        DeviceCredential credential,
        CancellationToken cancellationToken)
    {
        switch (item.Envelope.OperationType)
        {
            case UploadOperationType.Heartbeat:
                await serverClient.SendHeartbeatAsync(
                    credential,
                    await ReadJsonAsync<DeviceHeartbeatRequest>(item.PayloadPath, cancellationToken).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);
                break;
            case UploadOperationType.CurrentProcesses:
                await serverClient.ReplaceCurrentProcessesAsync(
                    credential,
                    await ReadJsonAsync<CurrentProcessesRequest>(item.PayloadPath, cancellationToken).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);
                break;
            case UploadOperationType.ProcessEvents:
                await serverClient.SendProcessEventsAsync(
                    credential,
                    await ReadJsonAsync<ProcessEventsRequest>(item.PayloadPath, cancellationToken).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);
                break;
            case UploadOperationType.AgentEvents:
                await serverClient.SendAgentEventsAsync(
                    credential,
                    await ReadJsonAsync<AgentEventsRequest>(item.PayloadPath, cancellationToken).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);
                break;
            case UploadOperationType.Screenshot:
                var metadata = JsonSerializer.Deserialize<ScreenshotQueueMetadata>(
                    item.Envelope.MetadataJson ?? string.Empty,
                    JsonOptions) ?? throw new InvalidDataException("Screenshot queue metadata is missing.");
                await serverClient.UploadScreenshotAsync(
                    credential,
                    new ScreenshotUpload(
                        metadata.CaptureId,
                        metadata.CapturedAt,
                        metadata.MonitorIndex,
                        metadata.Width,
                        metadata.Height,
                        item.PayloadPath,
                        metadata.MimeType),
                    cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidDataException("Queue operation type is unsupported.");
        }
    }

    private async Task<DateTimeOffset> ScheduleRetryAsync(
        UploadQueueItem item,
        CancellationToken cancellationToken)
    {
        var attemptCount = checked(item.Envelope.AttemptCount + 1);
        var delay = RetryBackoffCalculator.Calculate(
            attemptCount,
            TimeSpan.FromSeconds(settings.RetryMinimumSeconds),
            TimeSpan.FromSeconds(settings.RetryMaximumSeconds),
            _jitterSource());
        var next = timeProvider.GetUtcNow() + delay;
        await queue.MarkRetryAsync(
            item.Envelope.OperationId,
            attemptCount,
            next,
            cancellationToken).ConfigureAwait(false);
        return next;
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            useAsync: true);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("Queue JSON payload is empty.");
    }
}
