using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Interfaces;

public interface IXugarServerClient
{
    Task<DeviceEnrollmentResponse> EnrollDeviceAsync(
        DeviceEnrollmentRequest request,
        string enrollmentToken,
        CancellationToken cancellationToken);

    Task SendHeartbeatAsync(DeviceCredential credential, DeviceHeartbeatRequest request, CancellationToken cancellationToken);

    Task ReplaceCurrentProcessesAsync(DeviceCredential credential, CurrentProcessesRequest request, CancellationToken cancellationToken);

    Task SendProcessEventsAsync(DeviceCredential credential, ProcessEventsRequest request, CancellationToken cancellationToken);

    Task UploadScreenshotAsync(DeviceCredential credential, ScreenshotUpload upload, CancellationToken cancellationToken);

    Task SendAgentEventsAsync(DeviceCredential credential, AgentEventsRequest request, CancellationToken cancellationToken);

    Task<MonitoringPolicy> GetPolicyAsync(DeviceCredential credential, CancellationToken cancellationToken);
}
