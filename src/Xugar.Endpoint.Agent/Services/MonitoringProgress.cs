namespace Xugar.Endpoint.Agent.Services;

public sealed record MonitoringProgress(
    bool IsRunning,
    string Status,
    string Detail,
    DateTimeOffset? LastScreenshotUtc,
    DateTimeOffset? LastProcessSnapshotUtc);
