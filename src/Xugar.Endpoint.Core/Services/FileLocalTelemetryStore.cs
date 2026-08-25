using System.Text;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public sealed class FileLocalTelemetryStore : ILocalTelemetryStore, IDisposable
{
    private readonly string _dataRoot;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public FileLocalTelemetryStore(string dataRoot)
    {
        _dataRoot = StoragePaths.ResolveDataRoot(dataRoot);
    }

    public Task WriteProcessSnapshotAsync(
        ProcessSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return WriteAsync("processSnapshot", snapshot.CapturedAtUtc, snapshot, cancellationToken);
    }

    public Task WriteScreenshotMetadataAsync(
        IReadOnlyList<ScreenshotMetadata> screenshots,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(screenshots);
        if (screenshots.Count == 0)
        {
            return Task.CompletedTask;
        }

        return WriteAsync(
            "screenshotCapture",
            screenshots[0].CapturedAtUtc,
            screenshots,
            cancellationToken);
    }

    public Task WriteOperationalEventAsync(
        OperationalEvent operationalEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationalEvent);
        return WriteAsync(
            "operationalEvent",
            operationalEvent.TimestampUtc,
            operationalEvent,
            cancellationToken);
    }

    public void Dispose()
    {
        _writeGate.Dispose();
    }

    private async Task WriteAsync<T>(
        string recordType,
        DateTimeOffset timestampUtc,
        T payload,
        CancellationToken cancellationToken)
    {
        var line = TelemetryJsonSerializer.Serialize(recordType, timestampUtc, payload);
        var telemetryPath = StoragePaths.GetTelemetryPath(_dataRoot, timestampUtc);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(telemetryPath)!);

            await using var stream = new FileStream(
                telemetryPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4_096,
                useAsync: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
