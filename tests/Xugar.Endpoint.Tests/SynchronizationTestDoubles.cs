using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

internal sealed class MemoryCredentialStore : IDeviceCredentialStore
{
    public DeviceCredential? Credential { get; set; }

    public int SaveCount { get; private set; }

    public Task<DeviceCredential?> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Credential);

    public Task SaveAsync(DeviceCredential credential, CancellationToken cancellationToken)
    {
        Credential = credential;
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        Credential = null;
        return Task.CompletedTask;
    }
}

internal sealed class StubServerClient : IXugarServerClient
{
    public DeviceEnrollmentResponse EnrollmentResponse { get; set; } =
        new(Guid.NewGuid(), "test-device-secret");

    public MonitoringPolicy Policy { get; set; } = new(
        1,
        true,
        true,
        300,
        true,
        60,
        "UTC",
        [new MonitoringScheduleWindow(1, "00:00", "23:59")]);

    public Exception? Failure { get; set; }

    public int EnrollmentCalls { get; private set; }

    public int UploadCalls { get; private set; }

    public DeviceCredential? LastCredential { get; private set; }

    public DeviceEnrollmentRequest? LastEnrollmentRequest { get; private set; }

    public Task<DeviceEnrollmentResponse> EnrollDeviceAsync(
        DeviceEnrollmentRequest request,
        string enrollmentToken,
        CancellationToken cancellationToken)
    {
        EnrollmentCalls++;
        LastEnrollmentRequest = request;
        ThrowIfConfigured();
        return Task.FromResult(EnrollmentResponse);
    }

    public Task SendHeartbeatAsync(DeviceCredential credential, DeviceHeartbeatRequest request, CancellationToken cancellationToken) =>
        Uploaded(credential);

    public Task ReplaceCurrentProcessesAsync(DeviceCredential credential, CurrentProcessesRequest request, CancellationToken cancellationToken) =>
        Uploaded(credential);

    public Task SendProcessEventsAsync(DeviceCredential credential, ProcessEventsRequest request, CancellationToken cancellationToken) =>
        Uploaded(credential);

    public Task UploadScreenshotAsync(DeviceCredential credential, ScreenshotUpload upload, CancellationToken cancellationToken) =>
        Uploaded(credential);

    public Task SendAgentEventsAsync(DeviceCredential credential, AgentEventsRequest request, CancellationToken cancellationToken) =>
        Uploaded(credential);

    public Task<MonitoringPolicy> GetPolicyAsync(DeviceCredential credential, CancellationToken cancellationToken)
    {
        LastCredential = credential;
        ThrowIfConfigured();
        return Task.FromResult(Policy);
    }

    private Task Uploaded(DeviceCredential credential)
    {
        LastCredential = credential;
        UploadCalls++;
        ThrowIfConfigured();
        return Task.CompletedTask;
    }

    private void ThrowIfConfigured()
    {
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
