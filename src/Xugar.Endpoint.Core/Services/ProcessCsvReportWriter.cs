using System.Globalization;
using System.Text;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public sealed class ProcessCsvReportWriter : IProcessReportWriter, IDisposable
{
    private static readonly string[] CurrentHeader =
    [
        "Timestamp",
        "ProcessName",
        "Category",
        "PID",
        "ExecutablePath",
        "ProductVersion",
        "WorkingSetMB",
        "IsForeground"
    ];

    private static readonly string[] EventsHeader =
    [
        "Timestamp",
        "Event",
        "ProcessName",
        "Category",
        "PID",
        "ExecutablePath"
    ];

    private static readonly string[] SummaryHeader =
    [
        "ProcessName",
        "Category",
        "FirstSeen",
        "LastSeen",
        "SampleCount",
        "PeakWorkingSetMB",
        "ForegroundSampleCount"
    ];

    private static readonly UTF8Encoding Utf8WithBom = new(
        encoderShouldEmitUTF8Identifier: true,
        throwOnInvalidBytes: true);

    private readonly string _dataRoot;
    private readonly string? _windowsDirectory;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private IReadOnlyList<ProcessSnapshotRecord>? _previousProcesses;
    private DateOnly? _summaryDate;
    private ProcessSummaryAggregator? _summaryAggregator;

    public ProcessCsvReportWriter(string dataRoot, string? windowsDirectory = null)
    {
        _dataRoot = StoragePaths.ResolveDataRoot(dataRoot);
        _windowsDirectory = windowsDirectory;
    }

    public async Task WriteSnapshotAsync(
        ProcessSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var failures = new List<Exception>();
            var events = _previousProcesses is null
                ? Array.Empty<ProcessLifecycleEvent>()
                : ProcessEventDetector.Detect(
                    _previousProcesses,
                    snapshot.Processes,
                    snapshot.CapturedAtUtc,
                    _windowsDirectory);

            var summaryAggregator = await GetSummaryAggregatorAsync(
                    snapshot.CapturedAtUtc,
                    failures,
                    cancellationToken)
                .ConfigureAwait(false);
            summaryAggregator.AddSnapshot(snapshot, _windowsDirectory);

            await CaptureFailureAsync(
                () => WriteCurrentReportAsync(snapshot, cancellationToken),
                failures,
                cancellationToken).ConfigureAwait(false);
            await CaptureFailureAsync(
                () => AppendEventsAsync(snapshot.CapturedAtUtc, events, cancellationToken),
                failures,
                cancellationToken).ConfigureAwait(false);
            await CaptureFailureAsync(
                () => WriteSummaryReportAsync(
                    snapshot.CapturedAtUtc,
                    summaryAggregator.GetRows(),
                    cancellationToken),
                failures,
                cancellationToken).ConfigureAwait(false);

            if (failures.Count > 0)
            {
                throw new AggregateException("One or more derived process CSV reports failed.", failures);
            }
        }
        finally
        {
            // The raw snapshot succeeded before reporting was requested. Advance the baseline even
            // if a derived CSV is temporarily locked or unavailable, avoiding delayed false events.
            _previousProcesses = snapshot.Processes.ToArray();
            _writeGate.Release();
        }
    }

    public void Dispose()
    {
        _writeGate.Dispose();
    }

    private async Task<ProcessSummaryAggregator> GetSummaryAggregatorAsync(
        DateTimeOffset timestampUtc,
        ICollection<Exception> failures,
        CancellationToken cancellationToken)
    {
        var snapshotDate = DateOnly.FromDateTime(timestampUtc.UtcDateTime);
        if (_summaryDate == snapshotDate && _summaryAggregator is not null)
        {
            return _summaryAggregator;
        }

        var aggregator = new ProcessSummaryAggregator();
        var summaryPath = StoragePaths.GetProcessSummaryCsvPath(_dataRoot, timestampUtc);
        if (File.Exists(summaryPath))
        {
            try
            {
                var csv = await File.ReadAllTextAsync(summaryPath, cancellationToken).ConfigureAwait(false);
                aggregator.Seed(ParseSummaryRows(csv));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
                // JSONL remains canonical. Continue generating the other reports and surface this
                // derived-report problem to the coordinator as an isolated warning.
                failures.Add(exception);
            }
        }

        _summaryDate = snapshotDate;
        _summaryAggregator = aggregator;
        return aggregator;
    }

    private async Task WriteCurrentReportAsync(
        ProcessSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var rows = new List<string>(snapshot.Processes.Count + 1)
        {
            CsvFormatter.CreateRow(CurrentHeader)
        };

        rows.AddRange(snapshot.Processes
            .OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.ProcessId)
            .Select(process => CsvFormatter.CreateRow(
                FormatTimestamp(snapshot.CapturedAtUtc),
                process.ProcessName,
                ProcessCategorizer.Categorize(process, _windowsDirectory).ToString(),
                process.ProcessId.ToString(CultureInfo.InvariantCulture),
                process.ExecutablePath,
                process.ProductVersion,
                FormatWorkingSet(process.WorkingSetBytes),
                process.IsForeground?.ToString())));

        var path = StoragePaths.GetProcessCurrentCsvPath(_dataRoot, snapshot.CapturedAtUtc);
        await WriteAtomicallyAsync(path, rows, cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendEventsAsync(
        DateTimeOffset timestampUtc,
        IReadOnlyList<ProcessLifecycleEvent> events,
        CancellationToken cancellationToken)
    {
        var path = StoragePaths.GetProcessEventsCsvPath(_dataRoot, timestampUtc);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;

        await using var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4_096,
            useAsync: true);
        await using var writer = new StreamWriter(stream, Utf8WithBom)
        {
            NewLine = "\r\n"
        };

        if (writeHeader)
        {
            await writer.WriteLineAsync(
                    CsvFormatter.CreateRow(EventsHeader).AsMemory(),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var processEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = CsvFormatter.CreateRow(
                FormatTimestamp(processEvent.TimestampUtc),
                processEvent.EventType.ToString().ToUpperInvariant(),
                processEvent.Process.ProcessName,
                processEvent.Category.ToString(),
                processEvent.Process.ProcessId.ToString(CultureInfo.InvariantCulture),
                processEvent.Process.ExecutablePath);
            await writer.WriteLineAsync(row.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteSummaryReportAsync(
        DateTimeOffset timestampUtc,
        IReadOnlyList<ProcessSummaryRow> summaries,
        CancellationToken cancellationToken)
    {
        var rows = new List<string>(summaries.Count + 1)
        {
            CsvFormatter.CreateRow(SummaryHeader)
        };

        rows.AddRange(summaries.Select(summary => CsvFormatter.CreateRow(
            summary.ProcessName,
            summary.Category.ToString(),
            FormatTimestamp(summary.FirstSeenUtc),
            FormatTimestamp(summary.LastSeenUtc),
            summary.SampleCount.ToString(CultureInfo.InvariantCulture),
            summary.PeakWorkingSetMb?.ToString("0.00", CultureInfo.InvariantCulture),
            summary.ForegroundSampleCount.ToString(CultureInfo.InvariantCulture))));

        var path = StoragePaths.GetProcessSummaryCsvPath(_dataRoot, timestampUtc);
        await WriteAtomicallyAsync(path, rows, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAtomicallyAsync(
        string destinationPath,
        IReadOnlyList<string> rows,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = StoragePaths.EnsureUnderRoot(
            _dataRoot,
            Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp"));

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4_096,
                             useAsync: true))
            await using (var writer = new StreamWriter(stream, Utf8WithBom)
                         {
                             NewLine = "\r\n"
                         })
            {
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(row.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
            }

            // The temporary file is in the same daily directory. Windows implements this
            // overwrite as a same-volume rename with replacement, so readers see either the
            // previous complete report or the new complete report.
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Retention will remove an inaccessible temporary file later.
            }
        }
    }

    private static async Task CaptureFailureAsync(
        Func<Task> operation,
        ICollection<Exception> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failures.Add(exception);
        }
    }

    private static IReadOnlyList<ProcessSummaryRow> ParseSummaryRows(string csv)
    {
        var rows = CsvFormatter.ParseRows(csv);
        if (rows.Count == 0 || !rows[0].SequenceEqual(SummaryHeader, StringComparer.Ordinal))
        {
            throw new FormatException("The existing process summary has an unexpected header.");
        }

        var summaries = new List<ProcessSummaryRow>(Math.Max(0, rows.Count - 1));
        foreach (var row in rows.Skip(1))
        {
            if (row.Count != SummaryHeader.Length ||
                !Enum.TryParse<ProcessCategory>(row[1], ignoreCase: false, out var category) ||
                !DateTimeOffset.TryParse(
                    row[2],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var firstSeenUtc) ||
                !DateTimeOffset.TryParse(
                    row[3],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var lastSeenUtc) ||
                !long.TryParse(row[4], NumberStyles.None, CultureInfo.InvariantCulture, out var sampleCount) ||
                !long.TryParse(row[6], NumberStyles.None, CultureInfo.InvariantCulture, out var foregroundCount))
            {
                throw new FormatException("The existing process summary contains an invalid row.");
            }

            double? peakWorkingSetMb = null;
            if (!string.IsNullOrEmpty(row[5]))
            {
                if (!double.TryParse(
                        row[5],
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var parsedPeakWorkingSetMb))
                {
                    throw new FormatException("The existing process summary contains an invalid peak working set.");
                }

                peakWorkingSetMb = parsedPeakWorkingSetMb;
            }

            summaries.Add(new ProcessSummaryRow(
                row[0],
                category,
                firstSeenUtc,
                lastSeenUtc,
                sampleCount,
                peakWorkingSetMb,
                foregroundCount));
        }

        return summaries;
    }

    private static string FormatTimestamp(DateTimeOffset timestampUtc) =>
        timestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? FormatWorkingSet(long? workingSetBytes) =>
        workingSetBytes is null
            ? null
            : (workingSetBytes.Value / 1_048_576d).ToString("0.00", CultureInfo.InvariantCulture);
}
