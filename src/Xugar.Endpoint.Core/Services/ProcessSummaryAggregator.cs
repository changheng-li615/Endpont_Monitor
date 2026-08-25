using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public sealed class ProcessSummaryAggregator
{
    private readonly Dictionary<string, MutableSummary> _summaries =
        new(StringComparer.OrdinalIgnoreCase);

    public void AddSnapshot(ProcessSnapshot snapshot, string? windowsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        foreach (var processGroup in snapshot.Processes.GroupBy(
                     process => process.ProcessName,
                     StringComparer.OrdinalIgnoreCase))
        {
            var processes = processGroup.ToArray();
            var peakWorkingSetMb = processes
                .Where(process => process.WorkingSetBytes is not null)
                .Select(process => process.WorkingSetBytes!.Value / 1_048_576d)
                .Cast<double?>()
                .DefaultIfEmpty(null)
                .Max();
            var wasForeground = processes.Any(process => process.IsForeground == true);

            if (!_summaries.TryGetValue(processGroup.Key, out var summary))
            {
                summary = new MutableSummary
                {
                    ProcessName = processes[0].ProcessName,
                    FirstSeenUtc = snapshot.CapturedAtUtc,
                    LastSeenUtc = snapshot.CapturedAtUtc
                };
                _summaries.Add(summary.ProcessName, summary);
            }

            foreach (var process in processes)
            {
                summary.ObserveCategory(ProcessCategorizer.Categorize(process, windowsDirectory));
            }

            summary.FirstSeenUtc = Min(summary.FirstSeenUtc, snapshot.CapturedAtUtc);
            summary.LastSeenUtc = Max(summary.LastSeenUtc, snapshot.CapturedAtUtc);
            summary.SampleCount++;
            summary.PeakWorkingSetMb = Max(summary.PeakWorkingSetMb, peakWorkingSetMb);
            if (wasForeground)
            {
                summary.ForegroundSampleCount++;
            }
        }
    }

    public void Seed(IEnumerable<ProcessSummaryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        foreach (var row in rows)
        {
            _summaries[row.ProcessName] = new MutableSummary
            {
                ProcessName = row.ProcessName,
                Category = row.Category,
                HasKnownCategory = row.Category != ProcessCategory.Unknown,
                CategoryConflict = row.Category == ProcessCategory.Unknown,
                FirstSeenUtc = row.FirstSeenUtc,
                LastSeenUtc = row.LastSeenUtc,
                SampleCount = row.SampleCount,
                PeakWorkingSetMb = row.PeakWorkingSetMb,
                ForegroundSampleCount = row.ForegroundSampleCount
            };
        }
    }

    public IReadOnlyList<ProcessSummaryRow> GetRows() =>
        _summaries.Values
            .OrderBy(summary => summary.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(summary => new ProcessSummaryRow(
                summary.ProcessName,
                summary.Category,
                summary.FirstSeenUtc,
                summary.LastSeenUtc,
                summary.SampleCount,
                summary.PeakWorkingSetMb,
                summary.ForegroundSampleCount))
            .ToArray();

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;

    private static double? Max(double? left, double? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return Math.Max(left.Value, right.Value);
    }

    private sealed class MutableSummary
    {
        public required string ProcessName { get; init; }

        public ProcessCategory Category { get; set; } = ProcessCategory.Unknown;

        public bool HasKnownCategory { get; set; }

        public bool CategoryConflict { get; set; }

        public DateTimeOffset FirstSeenUtc { get; set; }

        public DateTimeOffset LastSeenUtc { get; set; }

        public long SampleCount { get; set; }

        public double? PeakWorkingSetMb { get; set; }

        public long ForegroundSampleCount { get; set; }

        public void ObserveCategory(ProcessCategory observed)
        {
            if (observed == ProcessCategory.Unknown || CategoryConflict)
            {
                return;
            }

            if (!HasKnownCategory)
            {
                Category = observed;
                HasKnownCategory = true;
                return;
            }

            if (Category != observed)
            {
                Category = ProcessCategory.Unknown;
                CategoryConflict = true;
            }
        }
    }
}
