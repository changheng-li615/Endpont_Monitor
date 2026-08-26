using System.Text.Json;
using System.Text.Json.Serialization;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public sealed class FileUploadQueue : IUploadQueue, IDisposable
{
    private const int MaximumCorruptEntries = 10;
    private const int MaximumEnvelopeBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dataRoot;
    private readonly string _queueRoot;
    private readonly string _envelopesDirectory;
    private readonly string _payloadsDirectory;
    private readonly string _corruptDirectory;
    private readonly ServerSyncSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _maintenanceDroppedScreenshotCount;

    public FileUploadQueue(string dataRoot, ServerSyncSettings settings, TimeProvider timeProvider)
    {
        _dataRoot = StoragePaths.ResolveDataRoot(dataRoot);
        _queueRoot = StoragePaths.GetUploadQueueDirectory(_dataRoot);
        _envelopesDirectory = StoragePaths.EnsureUnderRoot(_dataRoot, Path.Combine(_queueRoot, "envelopes"));
        _payloadsDirectory = StoragePaths.EnsureUnderRoot(_dataRoot, Path.Combine(_queueRoot, "payloads"));
        _corruptDirectory = StoragePaths.EnsureUnderRoot(_dataRoot, Path.Combine(_queueRoot, "corrupt"));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<UploadQueueEnqueueResult> EnqueueJsonAsync<T>(
        Guid operationId,
        UploadOperationType operationType,
        DateTimeOffset createdAtUtc,
        T payload,
        bool coalesce,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return await EnqueueBytesAsync(
            operationId,
            operationType,
            createdAtUtc,
            bytes,
            ".json",
            "application/json",
            null,
            coalesce,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<UploadQueueEnqueueResult> EnqueueFileAsync(
        Guid operationId,
        UploadOperationType operationType,
        DateTimeOffset createdAtUtc,
        string sourcePath,
        string contentType,
        string metadataJson,
        CancellationToken cancellationToken)
    {
        if (operationType != UploadOperationType.Screenshot)
        {
            throw new ArgumentException("Only screenshot queue entries may use a file payload.", nameof(operationType));
        }

        var safeSource = StoragePaths.EnsureUnderRoot(_dataRoot, sourcePath);
        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            _ => throw new ArgumentException("Only PNG and JPEG screenshot payloads are supported.", nameof(contentType))
        };
        var fileLength = new FileInfo(safeSource).Length;
        if (fileLength > _settings.QueueMaxBytes)
        {
            return new UploadQueueEnqueueResult(false, [UploadOperationType.Screenshot]);
        }

        var bytes = await File.ReadAllBytesAsync(safeSource, cancellationToken).ConfigureAwait(false);
        return await EnqueueBytesAsync(
            operationId,
            operationType,
            createdAtUtc,
            bytes,
            extension,
            contentType,
            metadataJson,
            coalesce: false,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UploadQueueItem>> GetReadyAsync(
        DateTimeOffset nowUtc,
        int maximumItems,
        CancellationToken cancellationToken)
    {
        if (maximumItems < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadAndMaintainAsync(nowUtc, cancellationToken).ConfigureAwait(false);
            return entries
                .Where(entry => entry.Envelope.NextAttemptAtUtc <= nowUtc)
                .OrderBy(entry => entry.Envelope.NextAttemptAtUtc)
                .ThenBy(entry => entry.Envelope.CreatedAtUtc)
                .ThenBy(entry => entry.Envelope.OperationId)
                .Take(maximumItems)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkRetryAsync(
        Guid operationId,
        int attemptCount,
        DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetEnvelopePath(operationId);
            if (!File.Exists(path))
            {
                return;
            }

            var envelope = await ReadEnvelopeAsync(path, cancellationToken).ConfigureAwait(false);
            if (envelope is null)
            {
                return;
            }

            var updated = envelope with
            {
                AttemptCount = attemptCount,
                NextAttemptAtUtc = nextAttemptAtUtc
            };
            await AtomicFile.WriteAllTextAsync(
                _dataRoot,
                path,
                JsonSerializer.Serialize(updated, JsonOptions),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetEnvelopePath(operationId);
            var envelope = File.Exists(path)
                ? await ReadEnvelopeAsync(path, cancellationToken).ConfigureAwait(false)
                : null;
            File.Delete(path);
            if (envelope is not null)
            {
                File.Delete(ResolvePayloadPath(envelope.PayloadRelativePath));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UploadQueueSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadAndMaintainAsync(_timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            var droppedScreenshots = _maintenanceDroppedScreenshotCount;
            _maintenanceDroppedScreenshotCount = 0;
            return new UploadQueueSnapshot(
                entries.Count,
                CalculateTotalBytes(entries),
                Directory.Exists(_corruptDirectory)
                    ? Directory.EnumerateFiles(_corruptDirectory, "*.json").Count()
                    : 0,
                droppedScreenshots);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<UploadQueueEnqueueResult> EnqueueBytesAsync(
        Guid operationId,
        UploadOperationType operationType,
        DateTimeOffset createdAtUtc,
        ReadOnlyMemory<byte> payload,
        string extension,
        string contentType,
        string? metadataJson,
        bool coalesce,
        CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("A queue operation ID is required.", nameof(operationId));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectories();
            var now = _timeProvider.GetUtcNow();
            var existing = await LoadAndMaintainAsync(now, cancellationToken).ConfigureAwait(false);
            var dropped = new List<UploadOperationType>();
            if (coalesce)
            {
                foreach (var obsolete in existing.Where(item => item.Envelope.OperationType == operationType))
                {
                    DeleteEntry(obsolete);
                    dropped.Add(obsolete.Envelope.OperationType);
                }
                existing.RemoveAll(item => item.Envelope.OperationType == operationType);
            }

            var payloadName = $"{operationId:N}{extension}";
            var relativePayload = Path.Combine("payloads", payloadName).Replace('\\', '/');
            var payloadPath = ResolvePayloadPath(relativePayload);
            await AtomicFile.WriteAllBytesAsync(_dataRoot, payloadPath, payload, cancellationToken)
                .ConfigureAwait(false);
            var envelope = new UploadQueueEnvelope(
                1,
                operationId,
                operationType,
                createdAtUtc.ToUniversalTime(),
                0,
                now,
                relativePayload,
                contentType,
                metadataJson);
            var envelopePath = GetEnvelopePath(operationId);
            await AtomicFile.WriteAllTextAsync(
                _dataRoot,
                envelopePath,
                JsonSerializer.Serialize(envelope, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            var created = new UploadQueueItem(envelope, payloadPath);
            existing.Add(created);

            while (existing.Count > _settings.QueueMaxItems ||
                   CalculateTotalBytes(existing) > _settings.QueueMaxBytes)
            {
                var victim = existing
                    .OrderBy(item => EvictionPriority(item.Envelope.OperationType))
                    .ThenBy(item => item.Envelope.CreatedAtUtc)
                    .ThenBy(item => item.Envelope.OperationId)
                    .First();
                DeleteEntry(victim);
                existing.Remove(victim);
                dropped.Add(victim.Envelope.OperationType);
            }

            return new UploadQueueEnqueueResult(existing.Contains(created), dropped);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<UploadQueueItem>> LoadAndMaintainAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        EnsureDirectories();
        var entries = new List<UploadQueueItem>();
        var referencedPayloads = new HashSet<string>(PathComparer);
        foreach (var envelopePath in Directory.EnumerateFiles(_envelopesDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var envelope = await ReadEnvelopeAsync(envelopePath, cancellationToken).ConfigureAwait(false);
            if (envelope is null)
            {
                continue;
            }

            string payloadPath;
            try
            {
                payloadPath = ResolvePayloadPath(envelope.PayloadRelativePath);
            }
            catch (ArgumentException)
            {
                QuarantineEnvelope(envelopePath);
                continue;
            }

            if (envelope.Version != 1 || envelope.OperationId == Guid.Empty || !File.Exists(payloadPath))
            {
                QuarantineEnvelope(envelopePath);
                continue;
            }

            if (nowUtc - envelope.CreatedAtUtc > TimeSpan.FromHours(_settings.QueueMaxAgeHours))
            {
                File.Delete(envelopePath);
                File.Delete(payloadPath);
                if (envelope.OperationType == UploadOperationType.Screenshot)
                {
                    _maintenanceDroppedScreenshotCount++;
                }
                continue;
            }

            referencedPayloads.Add(payloadPath);
            entries.Add(new UploadQueueItem(envelope, payloadPath));
        }

        foreach (var payloadPath in Directory.EnumerateFiles(_payloadsDirectory))
        {
            if (!referencedPayloads.Contains(payloadPath))
            {
                File.Delete(payloadPath);
            }
        }
        TrimCorruptDirectory();
        return entries;
    }

    private async Task<UploadQueueEnvelope?> ReadEnvelopeAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            if (new FileInfo(path).Length > MaximumEnvelopeBytes)
            {
                File.Delete(path);
                return null;
            }
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<UploadQueueEnvelope>(json, JsonOptions);
        }
        catch (JsonException)
        {
            QuarantineEnvelope(path);
            return null;
        }
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(_envelopesDirectory);
        Directory.CreateDirectory(_payloadsDirectory);
        Directory.CreateDirectory(_corruptDirectory);
    }

    private string GetEnvelopePath(Guid operationId) =>
        StoragePaths.EnsureUnderRoot(_dataRoot, Path.Combine(_envelopesDirectory, $"{operationId:N}.json"));

    private string ResolvePayloadPath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Queue payload paths must be relative.", nameof(relativePath));
        }

        var path = StoragePaths.EnsureUnderRoot(_dataRoot, Path.Combine(_queueRoot, relativePath));
        var relativeToPayloadRoot = Path.GetRelativePath(_payloadsDirectory, path);
        if (Path.IsPathRooted(relativeToPayloadRoot) ||
            relativeToPayloadRoot.Equals("..", StringComparison.Ordinal) ||
            relativeToPayloadRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException("Queue payload path escapes the payload directory.", nameof(relativePath));
        }

        return path;
    }

    private void QuarantineEnvelope(string path)
    {
        EnsureDirectories();
        var target = StoragePaths.EnsureUnderRoot(
            _dataRoot,
            Path.Combine(_corruptDirectory, $"{Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}.json"));
        if (File.Exists(path))
        {
            File.Move(path, target);
        }
    }

    private void TrimCorruptDirectory()
    {
        var files = Directory.EnumerateFiles(_corruptDirectory, "*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        foreach (var file in files.Skip(MaximumCorruptEntries))
        {
            file.Delete();
        }
    }

    private static long CalculateTotalBytes(IEnumerable<UploadQueueItem> entries) =>
        entries.Sum(item =>
            (File.Exists(item.PayloadPath) ? new FileInfo(item.PayloadPath).Length : 0) +
            JsonSerializer.SerializeToUtf8Bytes(item.Envelope, JsonOptions).LongLength);

    private void DeleteEntry(UploadQueueItem item)
    {
        File.Delete(GetEnvelopePath(item.Envelope.OperationId));
        File.Delete(item.PayloadPath);
    }

    private static int EvictionPriority(UploadOperationType type) => type switch
    {
        UploadOperationType.Heartbeat => 0,
        UploadOperationType.CurrentProcesses => 0,
        UploadOperationType.AgentEvents => 1,
        UploadOperationType.Screenshot => 2,
        UploadOperationType.ProcessEvents => 3,
        _ => 4
    };

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
