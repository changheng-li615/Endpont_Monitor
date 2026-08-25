using System.Globalization;

namespace Xugar.Endpoint.Core.Services;

public static class ScreenshotFileName
{
    public static string Create(DateTimeOffset capturedAtUtc, int monitorIndex)
    {
        if (monitorIndex < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(monitorIndex), "Monitor index must be at least one.");
        }

        var timestamp = capturedAtUtc.UtcDateTime.ToString(
            "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture);

        return $"{timestamp}_monitor-{monitorIndex}.png";
    }
}
