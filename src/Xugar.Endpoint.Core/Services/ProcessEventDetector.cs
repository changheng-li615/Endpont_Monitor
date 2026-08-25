using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public static class ProcessEventDetector
{
    public static IReadOnlyList<ProcessLifecycleEvent> Detect(
        IReadOnlyList<ProcessSnapshotRecord> previous,
        IReadOnlyList<ProcessSnapshotRecord> current,
        DateTimeOffset timestampUtc,
        string? windowsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var previousByPid = previous
            .GroupBy(process => process.ProcessId)
            .ToDictionary(group => group.Key, group => group.First());
        var currentByPid = current
            .GroupBy(process => process.ProcessId)
            .ToDictionary(group => group.Key, group => group.First());
        var events = new List<ProcessLifecycleEvent>();

        foreach (var previousProcess in previousByPid.Values
                     .OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(process => process.ProcessId))
        {
            if (!currentByPid.TryGetValue(previousProcess.ProcessId, out var currentProcess) ||
                !IsSameProcess(previousProcess, currentProcess))
            {
                events.Add(new ProcessLifecycleEvent(
                    timestampUtc,
                    ProcessLifecycleEventType.Stop,
                    previousProcess,
                    ProcessCategorizer.Categorize(previousProcess, windowsDirectory)));
            }
        }

        foreach (var currentProcess in currentByPid.Values
                     .OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(process => process.ProcessId))
        {
            if (!previousByPid.TryGetValue(currentProcess.ProcessId, out var previousProcess) ||
                !IsSameProcess(previousProcess, currentProcess))
            {
                events.Add(new ProcessLifecycleEvent(
                    timestampUtc,
                    ProcessLifecycleEventType.Start,
                    currentProcess,
                    ProcessCategorizer.Categorize(currentProcess, windowsDirectory)));
            }
        }

        return events;
    }

    public static bool IsSameProcess(
        ProcessSnapshotRecord previous,
        ProcessSnapshotRecord current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        if (previous.ProcessId != current.ProcessId)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(previous.ExecutablePath) &&
            !string.IsNullOrWhiteSpace(current.ExecutablePath))
        {
            return NormalizePath(previous.ExecutablePath)
                .Equals(NormalizePath(current.ExecutablePath), StringComparison.OrdinalIgnoreCase);
        }

        return previous.ProcessName.Equals(current.ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return path.Trim();
        }
    }
}
