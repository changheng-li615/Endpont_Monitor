namespace Xugar.Endpoint.Core.Models;

public sealed record ProcessSnapshot(
    DateTimeOffset CapturedAtUtc,
    DeviceContext Device,
    IReadOnlyList<ProcessSnapshotRecord> Processes);
