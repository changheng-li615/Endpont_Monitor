using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public sealed class DeviceEnrollmentService(
    IInstallationIdentityStore installationIdentityStore,
    IDeviceCredentialStore credentialStore,
    IDeviceContextProvider deviceContextProvider,
    IXugarServerClient serverClient,
    TimeProvider timeProvider,
    ServerSyncSettings settings)
{
    public async Task<DeviceCredential> EnsureEnrolledAsync(CancellationToken cancellationToken)
    {
        var existing = await credentialStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.Validate();
            return existing;
        }

        if (string.IsNullOrWhiteSpace(settings.EnrollmentToken))
        {
            throw new InvalidOperationException(
                "Server synchronization requires an enrollment token until this installation is enrolled.");
        }

        var now = timeProvider.GetUtcNow();
        var installationId = await installationIdentityStore
            .GetOrCreateInstallationIdAsync(cancellationToken)
            .ConfigureAwait(false);
        var device = deviceContextProvider.GetCurrent(now);
        var response = await serverClient.EnrollDeviceAsync(
            new DeviceEnrollmentRequest(
                installationId,
                device.MachineName,
                device.UserName,
                null,
                device.OperatingSystem,
                device.ApplicationVersion),
            settings.EnrollmentToken,
            cancellationToken).ConfigureAwait(false);

        var credential = new DeviceCredential(response.DeviceId, response.DeviceSecret);
        credential.Validate();
        await credentialStore.SaveAsync(credential, cancellationToken).ConfigureAwait(false);
        return credential;
    }
}
