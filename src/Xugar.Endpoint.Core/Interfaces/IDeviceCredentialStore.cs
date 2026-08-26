using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Interfaces;

public interface IDeviceCredentialStore
{
    Task<DeviceCredential?> ReadAsync(CancellationToken cancellationToken);

    Task SaveAsync(DeviceCredential credential, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
