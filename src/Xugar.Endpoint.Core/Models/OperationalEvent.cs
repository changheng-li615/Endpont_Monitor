namespace Xugar.Endpoint.Core.Models;

public sealed record OperationalEvent(
    DateTimeOffset TimestampUtc,
    string Category,
    string Level,
    string Message,
    IReadOnlyDictionary<string, object?>? Properties = null);
