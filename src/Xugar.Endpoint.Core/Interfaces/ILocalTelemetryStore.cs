using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Interfaces;

public interface ILocalTelemetryStore
{
    Task WriteProcessSnapshotAsync(ProcessSnapshot snapshot, CancellationToken cancellationToken);

    Task WriteScreenshotMetadataAsync(
        IReadOnlyList<ScreenshotMetadata> screenshots,
        CancellationToken cancellationToken);

    Task WriteOperationalEventAsync(OperationalEvent operationalEvent, CancellationToken cancellationToken);
}
