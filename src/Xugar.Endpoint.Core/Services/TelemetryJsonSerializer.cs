using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xugar.Endpoint.Core.Services;

public static class TelemetryJsonSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string Serialize<T>(
        string recordType,
        DateTimeOffset writtenAtUtc,
        T payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        ArgumentNullException.ThrowIfNull(payload);

        return JsonSerializer.Serialize(
            new TelemetryEnvelope<T>(recordType, writtenAtUtc, payload),
            SerializerOptions);
    }

    private sealed record TelemetryEnvelope<T>(
        string RecordType,
        DateTimeOffset WrittenAtUtc,
        T Payload);
}
