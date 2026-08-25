namespace Xugar.Endpoint.Core.Models;

public sealed record ScreenshotMetadata(
    DateTimeOffset CapturedAtUtc,
    int MonitorIndex,
    string FilePath,
    int PixelWidth,
    int PixelHeight);
