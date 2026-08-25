namespace Xugar.Endpoint.Core.Models;

public sealed record DeviceContext(
    DateTimeOffset CapturedAtUtc,
    string MachineName,
    string UserName,
    string OperatingSystem,
    string ApplicationVersion);
