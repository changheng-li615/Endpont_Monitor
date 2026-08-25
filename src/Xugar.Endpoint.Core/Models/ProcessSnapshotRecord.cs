namespace Xugar.Endpoint.Core.Models;

public sealed record ProcessSnapshotRecord(
    string ProcessName,
    int ProcessId,
    string? ExecutablePath,
    string? FileVersion,
    string? ProductVersion,
    long? WorkingSetBytes,
    bool? IsForeground);
