using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public static class SynchronizationPayloadMapper
{
    public static CurrentProcessesRequest MapCurrentProcesses(ProcessSnapshot snapshot, int maximumProcesses = 512)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maximumProcesses is < 1 or > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumProcesses));
        }

        return new CurrentProcessesRequest(
            snapshot.CapturedAtUtc,
            snapshot.Processes
                .Take(maximumProcesses)
                .Select(MapProcess)
                .ToArray());
    }

    public static IReadOnlyList<ServerProcessEvent> MapProcessEvents(
        IReadOnlyList<ProcessLifecycleEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events.Select(processEvent =>
        {
            var process = processEvent.Process;
            return new ServerProcessEvent(
                Guid.NewGuid(),
                processEvent.TimestampUtc,
                processEvent.EventType == ProcessLifecycleEventType.Start ? "START" : "STOP",
                process.ProcessName,
                process.ProcessId,
                NullIfWhiteSpace(process.ExecutablePath),
                NullIfWhiteSpace(process.ProductVersion),
                ToMegabytes(process.WorkingSetBytes),
                process.IsForeground);
        }).ToArray();
    }

    public static ServerProcessRecord MapProcess(ProcessSnapshotRecord process) =>
        new(
            process.ProcessName,
            process.ProcessId,
            NullIfWhiteSpace(process.ExecutablePath),
            NullIfWhiteSpace(process.ProductVersion),
            ToMegabytes(process.WorkingSetBytes),
            process.IsForeground ?? false);

    private static double? ToMegabytes(long? bytes) =>
        bytes is null ? null : Math.Round(bytes.Value / 1024d / 1024d, 3);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
