namespace Xugar.Endpoint.Core.Interfaces;

public interface IInstallationIdentityStore
{
    Task<Guid> GetOrCreateInstallationIdAsync(CancellationToken cancellationToken);
}
