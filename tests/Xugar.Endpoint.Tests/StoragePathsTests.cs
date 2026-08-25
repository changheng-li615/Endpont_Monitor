using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class StoragePathsTests
{
    [Fact]
    public void DateAndScreenshotPathsRemainUnderConfiguredRoot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var timestamp = new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

        var screenshotDirectory = StoragePaths.GetScreenshotDirectory(
            temporaryDirectory.Path,
            timestamp);
        var relativePath = Path.GetRelativePath(temporaryDirectory.Path, screenshotDirectory);

        Assert.Equal(Path.Combine("2026-08-25", "screenshots"), relativePath);
    }

    [Fact]
    public void EscapingConfiguredRootIsRejected()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var outsidePath = Path.Combine(
            Path.GetDirectoryName(temporaryDirectory.Path)!,
            $"outside-{Guid.NewGuid():N}",
            "file.jsonl");

        Assert.Throws<ArgumentException>(
            () => StoragePaths.EnsureUnderRoot(temporaryDirectory.Path, outsidePath));
    }

    [Fact]
    public void RelativeDataRootIsRejected()
    {
        Assert.Throws<ArgumentException>(() => StoragePaths.ResolveDataRoot("relative\\data"));
    }

    [Fact]
    public void ProcessCsvPathsRemainInsideTheConfiguredDailyDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var timestamp = new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

        var paths = new[]
        {
            StoragePaths.GetProcessCurrentCsvPath(temporaryDirectory.Path, timestamp),
            StoragePaths.GetProcessEventsCsvPath(temporaryDirectory.Path, timestamp),
            StoragePaths.GetProcessSummaryCsvPath(temporaryDirectory.Path, timestamp)
        };

        Assert.All(
            paths,
            path => Assert.Equal(
                Path.Combine("2026-08-25", Path.GetFileName(path)),
                Path.GetRelativePath(temporaryDirectory.Path, path)));
    }
}
