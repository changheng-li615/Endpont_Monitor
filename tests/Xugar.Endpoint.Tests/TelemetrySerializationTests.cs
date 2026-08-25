using System.Text.Json;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class TelemetrySerializationTests
{
    [Fact]
    public void ProcessSnapshotSerializesAsOneStructuredCamelCaseRecord()
    {
        var timestamp = new DateTimeOffset(2026, 8, 25, 1, 45, 0, TimeSpan.Zero);
        var snapshot = new ProcessSnapshot(
            timestamp,
            new DeviceContext(timestamp, "DEVICE-1", "DOMAIN\\employee", "Windows", "1.0.0"),
            [new ProcessSnapshotRecord("notepad", 42, null, null, null, 12_345, true)]);

        var json = TelemetryJsonSerializer.Serialize("processSnapshot", timestamp, snapshot);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("processSnapshot", document.RootElement.GetProperty("recordType").GetString());
        Assert.Equal(
            "notepad",
            document.RootElement
                .GetProperty("payload")
                .GetProperty("processes")[0]
                .GetProperty("processName")
                .GetString());
        Assert.DoesNotContain("commandLine", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.NewLine, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileStoreAppendsValidJsonLinesToTheExpectedDateDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var store = new FileLocalTelemetryStore(temporaryDirectory.Path);
        var timestamp = new DateTimeOffset(2026, 8, 25, 1, 45, 0, TimeSpan.Zero);
        var operationalEvent = new OperationalEvent(
            timestamp,
            "monitoring",
            "Information",
            "Monitoring started.");

        await store.WriteOperationalEventAsync(operationalEvent, CancellationToken.None);

        var telemetryPath = StoragePaths.GetTelemetryPath(temporaryDirectory.Path, timestamp);
        var lines = await File.ReadAllLinesAsync(telemetryPath);
        Assert.Single(lines);
        using var document = JsonDocument.Parse(lines[0]);
        Assert.Equal("operationalEvent", document.RootElement.GetProperty("recordType").GetString());
    }
}
