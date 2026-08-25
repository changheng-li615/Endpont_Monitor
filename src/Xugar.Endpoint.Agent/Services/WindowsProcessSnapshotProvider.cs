using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Agent.Services;

public sealed class WindowsProcessSnapshotProvider : IProcessSnapshotProvider
{
    public Task<ProcessSnapshot> CaptureAsync(
        DeviceContext deviceContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceContext);

        return Task.Run(
            () => Capture(deviceContext, cancellationToken),
            cancellationToken);
    }

    private static ProcessSnapshot Capture(
        DeviceContext deviceContext,
        CancellationToken cancellationToken)
    {
        var records = new List<ProcessSnapshotRecord>();
        var foregroundWindow = GetForegroundWindow();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int processId;
                try
                {
                    processId = process.Id;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                var processName = TryRead(() => process.ProcessName) ?? "unavailable";
                var workingSetBytes = TryReadNullable(() => process.WorkingSet64);
                var processWindow = TryReadNullable(() => process.MainWindowHandle);
                bool? isForeground = processWindow is null
                    ? null
                    : foregroundWindow != IntPtr.Zero && processWindow.Value == foregroundWindow;

                string? executablePath = null;
                string? fileVersion = null;
                string? productVersion = null;
                try
                {
                    var mainModule = process.MainModule;
                    executablePath = mainModule?.FileName;
                    fileVersion = mainModule?.FileVersionInfo.FileVersion;
                    productVersion = mainModule?.FileVersionInfo.ProductVersion;
                }
                catch (Exception exception) when (IsExpectedProcessException(exception))
                {
                    // Protected or short-lived processes retain nullable metadata.
                }

                records.Add(new ProcessSnapshotRecord(
                    processName,
                    processId,
                    executablePath,
                    fileVersion,
                    productVersion,
                    workingSetBytes,
                    isForeground));
            }
        }

        var orderedRecords = records
            .OrderBy(record => record.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.ProcessId)
            .ToArray();

        return new ProcessSnapshot(deviceContext.CapturedAtUtc, deviceContext, orderedRecords);
    }

    private static string? TryRead(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (IsExpectedProcessException(exception))
        {
            return null;
        }
    }

    private static T? TryReadNullable<T>(Func<T> read)
        where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (IsExpectedProcessException(exception))
        {
            return null;
        }
    }

    private static bool IsExpectedProcessException(Exception exception) =>
        exception is Win32Exception
            or InvalidOperationException
            or NotSupportedException;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
