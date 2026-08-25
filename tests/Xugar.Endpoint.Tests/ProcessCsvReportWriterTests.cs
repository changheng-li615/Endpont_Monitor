using System.Text;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class ProcessCsvReportWriterTests
{
    private static readonly DateTimeOffset FirstTimestamp =
        new(2026, 8, 25, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CurrentReportContainsLatestSnapshotAndExcelReadableUtf8Header()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var writer = CreateWriter(temporaryDirectory.Path);
        await writer.WriteSnapshotAsync(
            CreateSnapshot(
                FirstTimestamp,
                CreateProcess("old-app", 10, @"C:\Apps\old-app.exe", "1.0", 10, false)),
            CancellationToken.None);
        await writer.WriteSnapshotAsync(
            CreateSnapshot(
                FirstTimestamp.AddMinutes(1),
                CreateProcess("new,app", 20, @"C:\Apps\new-app.exe", "2.0", 25, true)),
            CancellationToken.None);

        var path = StoragePaths.GetProcessCurrentCsvPath(
            temporaryDirectory.Path,
            FirstTimestamp);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var rows = CsvFormatter.ParseRows(await File.ReadAllTextAsync(path));

        Assert.Equal(2, rows.Count);
        Assert.Equal(
            ["Timestamp", "ProcessName", "Category", "PID", "ExecutablePath", "ProductVersion", "WorkingSetMB", "IsForeground"],
            rows[0]);
        Assert.Equal("new,app", rows[1][1]);
        Assert.Equal("Application", rows[1][2]);
        Assert.Equal("20", rows[1][3]);
        Assert.Equal("25.00", rows[1][6]);
        Assert.Equal("True", rows[1][7]);
        Assert.DoesNotContain("old-app", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitialSnapshotCreatesOnlyEventsHeaderWithoutFalseStarts()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var writer = CreateWriter(temporaryDirectory.Path);

        await writer.WriteSnapshotAsync(
            CreateSnapshot(
                FirstTimestamp,
                CreateProcess("one", 1, @"C:\Apps\one.exe"),
                CreateProcess("two", 2, @"C:\Apps\two.exe")),
            CancellationToken.None);

        var path = StoragePaths.GetProcessEventsCsvPath(temporaryDirectory.Path, FirstTimestamp);
        var rows = CsvFormatter.ParseRows(await File.ReadAllTextAsync(path));
        Assert.Single(rows);
        Assert.Equal(["Timestamp", "Event", "ProcessName", "Category", "PID", "ExecutablePath"], rows[0]);
    }

    [Fact]
    public async Task EventReportAppendsStartAndStopRows()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var writer = CreateWriter(temporaryDirectory.Path);
        var alwaysRunning = CreateProcess("always", 1, @"C:\Apps\always.exe");
        var shortLived = CreateProcess("short-lived", 2, @"C:\Apps\short-lived.exe");

        await writer.WriteSnapshotAsync(
            CreateSnapshot(FirstTimestamp, alwaysRunning),
            CancellationToken.None);
        await writer.WriteSnapshotAsync(
            CreateSnapshot(FirstTimestamp.AddMinutes(1), alwaysRunning, shortLived),
            CancellationToken.None);
        await writer.WriteSnapshotAsync(
            CreateSnapshot(FirstTimestamp.AddMinutes(2), alwaysRunning),
            CancellationToken.None);

        var path = StoragePaths.GetProcessEventsCsvPath(temporaryDirectory.Path, FirstTimestamp);
        var rows = CsvFormatter.ParseRows(await File.ReadAllTextAsync(path));
        Assert.Equal(3, rows.Count);
        Assert.Equal("START", rows[1][1]);
        Assert.Equal("short-lived", rows[1][2]);
        Assert.Equal("STOP", rows[2][1]);
        Assert.Equal("short-lived", rows[2][2]);
    }

    [Fact]
    public async Task SummaryCountsPresenceSamplesRatherThanProcessInstances()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var writer = CreateWriter(temporaryDirectory.Path);

        await writer.WriteSnapshotAsync(
            CreateSnapshot(
                FirstTimestamp,
                CreateProcess("browser", 10, @"C:\Apps\browser.exe", workingSetMb: 10, isForeground: true)),
            CancellationToken.None);
        await writer.WriteSnapshotAsync(
            CreateSnapshot(
                FirstTimestamp.AddMinutes(1),
                CreateProcess("browser", 11, @"C:\Apps\browser.exe", workingSetMb: 20, isForeground: false),
                CreateProcess("browser", 12, @"C:\Apps\browser.exe", workingSetMb: 15, isForeground: false)),
            CancellationToken.None);

        var path = StoragePaths.GetProcessSummaryCsvPath(temporaryDirectory.Path, FirstTimestamp);
        var rows = CsvFormatter.ParseRows(await File.ReadAllTextAsync(path));
        Assert.Equal(2, rows.Count);
        Assert.Equal("browser", rows[1][0]);
        Assert.Equal("2", rows[1][4]);
        Assert.Equal("20.00", rows[1][5]);
        Assert.Equal("1", rows[1][6]);
    }

    [Fact]
    public async Task ExistingDailySummaryContinuesAcrossWriterRestartWithoutStartFlood()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var process = CreateProcess("work-app", 7, @"C:\Apps\work-app.exe", workingSetMb: 5);

        using (var firstWriter = CreateWriter(temporaryDirectory.Path))
        {
            await firstWriter.WriteSnapshotAsync(
                CreateSnapshot(FirstTimestamp, process),
                CancellationToken.None);
        }

        using (var restartedWriter = CreateWriter(temporaryDirectory.Path))
        {
            await restartedWriter.WriteSnapshotAsync(
                CreateSnapshot(FirstTimestamp.AddMinutes(1), process),
                CancellationToken.None);
        }

        var summaryPath = StoragePaths.GetProcessSummaryCsvPath(temporaryDirectory.Path, FirstTimestamp);
        var summaryRows = CsvFormatter.ParseRows(await File.ReadAllTextAsync(summaryPath));
        Assert.Equal("2", summaryRows[1][4]);

        var eventsPath = StoragePaths.GetProcessEventsCsvPath(temporaryDirectory.Path, FirstTimestamp);
        var eventRows = CsvFormatter.ParseRows(await File.ReadAllTextAsync(eventsPath));
        Assert.Single(eventRows);
    }

    [Fact]
    public async Task InaccessibleFieldsRemainEmptyAndCategoryIsUnknown()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var writer = CreateWriter(temporaryDirectory.Path);

        await writer.WriteSnapshotAsync(
            CreateSnapshot(
                FirstTimestamp,
                new ProcessSnapshotRecord("protected", 4, null, null, null, null, null)),
            CancellationToken.None);

        var path = StoragePaths.GetProcessCurrentCsvPath(temporaryDirectory.Path, FirstTimestamp);
        var rows = CsvFormatter.ParseRows(await File.ReadAllTextAsync(path));
        Assert.Equal("Unknown", rows[1][2]);
        Assert.Equal(string.Empty, rows[1][4]);
        Assert.Equal(string.Empty, rows[1][5]);
        Assert.Equal(string.Empty, rows[1][6]);
        Assert.Equal(string.Empty, rows[1][7]);
    }

    private static ProcessCsvReportWriter CreateWriter(string dataRoot)
    {
        var windowsDirectory = Path.Combine(dataRoot, "Windows");
        return new ProcessCsvReportWriter(dataRoot, windowsDirectory);
    }

    private static ProcessSnapshot CreateSnapshot(
        DateTimeOffset timestamp,
        params ProcessSnapshotRecord[] processes) =>
        new(
            timestamp,
            new DeviceContext(timestamp, "TEST-PC", "TEST\\user", "Windows", "1.0"),
            processes);

    private static ProcessSnapshotRecord CreateProcess(
        string name,
        int processId,
        string? executablePath,
        string? productVersion = null,
        double? workingSetMb = null,
        bool? isForeground = null) =>
        new(
            name,
            processId,
            executablePath,
            null,
            productVersion,
            workingSetMb is null ? null : (long)(workingSetMb.Value * 1_048_576),
            isForeground);
}
