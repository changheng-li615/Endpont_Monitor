namespace Xugar.Endpoint.Core.Models;

public sealed record CachedMonitoringPolicy(
    DateTimeOffset RetrievedAtUtc,
    MonitoringPolicy Policy);

public enum PolicyActivity
{
    ProcessMonitoring,
    ScreenshotCapture
}

public enum PolicyDecisionReason
{
    Allowed,
    SynchronizationDisabled,
    Unavailable,
    Expired,
    Invalid,
    MonitoringDisabled,
    ActivityDisabled,
    OutsideSchedule
}

public sealed record PolicyDecision(
    bool AllowLocalCapture,
    bool AllowSynchronization,
    PolicyDecisionReason Reason,
    int IntervalSeconds,
    int? PolicyVersion);
