using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Interfaces;

public interface IMonitoringPolicyCache
{
    Task<CachedMonitoringPolicy?> ReadAsync(CancellationToken cancellationToken);

    Task WriteAsync(CachedMonitoringPolicy policy, CancellationToken cancellationToken);
}
