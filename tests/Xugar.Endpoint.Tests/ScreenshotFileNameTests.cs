using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class ScreenshotFileNameTests
{
    [Fact]
    public void FilenameUsesUtcTimestampAndOneBasedMonitorIndex()
    {
        var timestamp = new DateTimeOffset(2026, 8, 25, 11, 45, 0, 123, TimeSpan.FromHours(10));

        var fileName = ScreenshotFileName.Create(timestamp, monitorIndex: 2);

        Assert.Equal("20260825T014500123Z_monitor-2.png", fileName);
    }

    [Fact]
    public void ZeroMonitorIndexIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ScreenshotFileName.Create(DateTimeOffset.UtcNow, monitorIndex: 0));
    }
}
