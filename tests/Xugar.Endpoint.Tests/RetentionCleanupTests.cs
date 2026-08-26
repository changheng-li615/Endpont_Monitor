using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class RetentionCleanupTests
{
    [Fact]
    public void CutoffUsesUtcAndConfiguredRetentionHours()
    {
        var now = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.FromHours(10));

        var cutoff = RetentionCleanup.CalculateCutoff(now, retentionHours: 24);

        Assert.Equal(new DateTimeOffset(2026, 8, 24, 2, 0, 0, TimeSpan.Zero), cutoff);
    }

    [Fact]
    public async Task CleanupDeletesOnlyFilesOlderThanCutoff()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var datedDirectory = Path.Combine(temporaryDirectory.Path, "2026-08-25");
        Directory.CreateDirectory(datedDirectory);
        var oldFile = Path.Combine(datedDirectory, "old.jsonl");
        var oldCsvFile = Path.Combine(datedDirectory, "old-report.csv");
        var newFile = Path.Combine(datedDirectory, "new.jsonl");
        await File.WriteAllTextAsync(oldFile, "old");
        await File.WriteAllTextAsync(oldCsvFile, "old csv");
        await File.WriteAllTextAsync(newFile, "new");
        File.SetLastWriteTimeUtc(oldFile, new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(oldCsvFile, new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newFile, new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc));

        var cleanup = new RetentionCleanup();
        var result = await cleanup.CleanupAsync(
            temporaryDirectory.Path,
            new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.False(File.Exists(oldFile));
        Assert.False(File.Exists(oldCsvFile));
        Assert.True(File.Exists(newFile));
        Assert.Equal(2, result.DeletedFiles);
        Assert.Equal(0, result.FailedFiles);
    }

    [Fact]
    public async Task CleanupDoesNotTouchAnExpiredFileOutsideItsRoot()
    {
        using var cleanupRoot = new TemporaryDirectory();
        using var outsideRoot = new TemporaryDirectory();
        var outsideFile = Path.Combine(outsideRoot.Path, "expired-but-outside.jsonl");
        await File.WriteAllTextAsync(outsideFile, "outside");
        File.SetLastWriteTimeUtc(outsideFile, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var cleanup = new RetentionCleanup();
        await cleanup.CleanupAsync(
            cleanupRoot.Path,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.True(File.Exists(outsideFile));
    }

    [Fact]
    public async Task PhaseOneRetentionDoesNotDeleteSynchronizationIdentityCredentialOrQueue()
    {
        using var directory = new TemporaryDirectory();
        var syncDirectory = StoragePaths.GetSynchronizationDirectory(directory.Path);
        Directory.CreateDirectory(syncDirectory);
        var identity = StoragePaths.GetInstallationIdentityPath(directory.Path);
        var credential = StoragePaths.GetDeviceCredentialPath(directory.Path);
        var queued = Path.Combine(StoragePaths.GetUploadQueueDirectory(directory.Path), "envelopes", "pending.json");
        Directory.CreateDirectory(Path.GetDirectoryName(queued)!);
        await File.WriteAllTextAsync(identity, Guid.NewGuid().ToString("D"));
        await File.WriteAllBytesAsync(credential, [1, 2, 3]);
        await File.WriteAllTextAsync(queued, "pending");
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(identity, old);
        File.SetLastWriteTimeUtc(credential, old);
        File.SetLastWriteTimeUtc(queued, old);

        await new RetentionCleanup().CleanupAsync(
            directory.Path,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.True(File.Exists(identity));
        Assert.True(File.Exists(credential));
        Assert.True(File.Exists(queued));
    }

    [Fact]
    public async Task CleanupRefusesAWholeFilesystemVolume()
    {
        var cleanup = new RetentionCleanup();
        var filesystemRoot = Path.GetPathRoot(Path.GetTempPath())!;

        await Assert.ThrowsAsync<ArgumentException>(
            () => cleanup.CleanupAsync(filesystemRoot, DateTimeOffset.UtcNow, CancellationToken.None));
    }
}
