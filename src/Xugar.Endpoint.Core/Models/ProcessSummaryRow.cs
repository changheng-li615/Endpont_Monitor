namespace Xugar.Endpoint.Core.Models;

public sealed record ProcessSummaryRow(
    string ProcessName,
    ProcessCategory Category,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    long SampleCount,
    double? PeakWorkingSetMb,
    long ForegroundSampleCount);
