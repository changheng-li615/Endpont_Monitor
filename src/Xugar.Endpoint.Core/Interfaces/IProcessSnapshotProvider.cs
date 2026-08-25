using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Interfaces;

public interface IProcessSnapshotProvider
{
    Task<ProcessSnapshot> CaptureAsync(
        DeviceContext deviceContext,
        CancellationToken cancellationToken);
}
