using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Interfaces;

public interface IScreenshotCapture
{
    Task<IReadOnlyList<ScreenshotMetadata>> CaptureAsync(
        string dataRoot,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken);
}
