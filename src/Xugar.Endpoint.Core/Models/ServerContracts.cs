namespace Xugar.Endpoint.Core.Models;

public sealed record DeviceEnrollmentRequest(
    Guid InstallationId,
    string Hostname,
    string? WindowsUser,
    string? WorkEmail,
    string OsVersion,
    string AgentVersion);

public sealed record DeviceEnrollmentResponse(Guid DeviceId, string DeviceSecret);

public sealed record DeviceHeartbeatRequest(
    DateTimeOffset OccurredAt,
    string AgentVersion,
    string OsVersion,
    long? UptimeSeconds);

public sealed record ServerProcessRecord(
    string ProcessName,
    int Pid,
    string? ExecutablePath,
    string? ProductVersion,
    double? WorkingSetMb,
    bool IsForeground);

public sealed record CurrentProcessesRequest(
    DateTimeOffset ObservedAt,
    IReadOnlyList<ServerProcessRecord> Processes);

public sealed record ServerProcessEvent(
    Guid ClientEventId,
    DateTimeOffset OccurredAt,
    string EventType,
    string ProcessName,
    int Pid,
    string? ExecutablePath,
    string? ProductVersion,
    double? WorkingSetMb,
    bool? IsForeground);

public sealed record ProcessEventsRequest(IReadOnlyList<ServerProcessEvent> Events);

public sealed record ServerAgentEvent(
    Guid ClientEventId,
    DateTimeOffset OccurredAt,
    string EventType,
    string Severity,
    string Message);

public sealed record AgentEventsRequest(IReadOnlyList<ServerAgentEvent> Events);

public sealed record MonitoringScheduleWindow(
    int DayOfWeek,
    string StartLocalTime,
    string EndLocalTime);

public sealed record MonitoringPolicy(
    int Version,
    bool MonitoringEnabled,
    bool ScreenshotEnabled,
    int ScreenshotIntervalSeconds,
    bool ProcessEnabled,
    int ProcessIntervalSeconds,
    string Timezone,
    IReadOnlyList<MonitoringScheduleWindow> ScheduleWindows);

public sealed record ScreenshotUpload(
    Guid CaptureId,
    DateTimeOffset CapturedAt,
    int MonitorIndex,
    int Width,
    int Height,
    string FilePath,
    string MimeType);
