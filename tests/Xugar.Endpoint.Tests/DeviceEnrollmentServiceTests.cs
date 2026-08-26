using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class DeviceEnrollmentServiceTests
{
    [Fact]
    public async Task EnrollsOnceAndPersistsIssuedCredential()
    {
        var installationId = Guid.NewGuid();
        var credentialStore = new MemoryCredentialStore();
        var server = new StubServerClient();
        var service = CreateService(installationId, credentialStore, server);

        var first = await service.EnsureEnrolledAsync(CancellationToken.None);
        var second = await service.EnsureEnrolledAsync(CancellationToken.None);

        Assert.Equal(server.EnrollmentResponse.DeviceId, first.DeviceId);
        Assert.Equal(first, second);
        Assert.Equal(1, server.EnrollmentCalls);
        Assert.Equal(1, credentialStore.SaveCount);
        Assert.Equal(installationId, server.LastEnrollmentRequest?.InstallationId);
    }

    [Fact]
    public async Task ExistingCredentialPreventsRepeatedEnrollment()
    {
        var existing = new DeviceCredential(Guid.NewGuid(), "already-protected");
        var store = new MemoryCredentialStore { Credential = existing };
        var server = new StubServerClient();

        var result = await CreateService(Guid.NewGuid(), store, server)
            .EnsureEnrolledAsync(CancellationToken.None);

        Assert.Equal(existing, result);
        Assert.Equal(0, server.EnrollmentCalls);
    }

    [Fact]
    public async Task MissingTokenAndServerFailureAreReportedWithoutCreatingCredential()
    {
        var store = new MemoryCredentialStore();
        var noToken = CreateService(Guid.NewGuid(), store, new StubServerClient(), string.Empty);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => noToken.EnsureEnrolledAsync(CancellationToken.None));

        var server = new StubServerClient
        {
            Failure = new XugarServerException(ServerFailureKind.Authentication, "HTTP 401", 401)
        };
        var invalid = CreateService(Guid.NewGuid(), store, server);
        await Assert.ThrowsAsync<XugarServerException>(
            () => invalid.EnsureEnrolledAsync(CancellationToken.None));
        Assert.Null(store.Credential);
    }

    private static DeviceEnrollmentService CreateService(
        Guid installationId,
        MemoryCredentialStore credentialStore,
        StubServerClient server,
        string token = "test-enrollment-token-not-production") =>
        new(
            new FixedIdentityStore(installationId),
            credentialStore,
            new FixedDeviceContextProvider(),
            server,
            new ManualTimeProvider(new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero)),
            new ServerSyncSettings { EnrollmentToken = token });

    private sealed class FixedIdentityStore(Guid id) : IInstallationIdentityStore
    {
        public Task<Guid> GetOrCreateInstallationIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult(id);
    }

    private sealed class FixedDeviceContextProvider : IDeviceContextProvider
    {
        public DeviceContext GetCurrent(DateTimeOffset capturedAtUtc) =>
            new(capturedAtUtc, "XUGAR-TEST", "tester", "Windows 11", "0.2.0");
    }
}
