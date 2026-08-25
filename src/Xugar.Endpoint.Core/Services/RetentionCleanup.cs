namespace Xugar.Endpoint.Core.Services;

public sealed class RetentionCleanup
{
    public static DateTimeOffset CalculateCutoff(DateTimeOffset nowUtc, int retentionHours)
    {
        if (retentionHours < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retentionHours),
                "Retention must be at least one hour.");
        }

        return nowUtc.ToUniversalTime().AddHours(-retentionHours);
    }

    public Task<RetentionCleanupResult> CleanupAsync(
        string dataRoot,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        var safeRoot = StoragePaths.ResolveDataRoot(dataRoot);
        return Task.Run(() => Cleanup(safeRoot, cutoffUtc, cancellationToken), cancellationToken);
    }

    private static RetentionCleanupResult Cleanup(
        string safeRoot,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(safeRoot))
        {
            return new RetentionCleanupResult(0, 0, 0);
        }

        var deletedFiles = 0;
        var failedFiles = 0;
        var skippedReparsePoints = 0;
        var pendingDirectories = new Stack<string>();
        var visitedDirectories = new List<string>();
        pendingDirectories.Push(safeRoot);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = StoragePaths.EnsureUnderRoot(safeRoot, pendingDirectories.Pop());
            visitedDirectories.Add(directory);

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var safeFile = StoragePaths.EnsureUnderRoot(safeRoot, file);

                    try
                    {
                        if ((File.GetAttributes(safeFile) & FileAttributes.ReparsePoint) != 0)
                        {
                            skippedReparsePoints++;
                            continue;
                        }

                        if (File.GetLastWriteTimeUtc(safeFile) < cutoffUtc.UtcDateTime)
                        {
                            File.Delete(safeFile);
                            deletedFiles++;
                        }
                    }
                    catch (Exception exception) when (IsRecoverableFileException(exception))
                    {
                        failedFiles++;
                    }
                }

                foreach (var childDirectory in Directory.EnumerateDirectories(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    var safeChild = StoragePaths.EnsureUnderRoot(safeRoot, childDirectory);
                    try
                    {
                        if ((File.GetAttributes(safeChild) & FileAttributes.ReparsePoint) != 0)
                        {
                            skippedReparsePoints++;
                            continue;
                        }

                        pendingDirectories.Push(safeChild);
                    }
                    catch (Exception exception) when (IsRecoverableFileException(exception))
                    {
                        failedFiles++;
                    }
                }
            }
            catch (Exception exception) when (IsRecoverableFileException(exception))
            {
                failedFiles++;
            }
        }

        foreach (var directory in visitedDirectories
                     .Where(path => !path.Equals(safeRoot, PathComparison))
                     .OrderByDescending(path => path.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, recursive: false);
                }
            }
            catch (Exception exception) when (IsRecoverableFileException(exception))
            {
                failedFiles++;
            }
        }

        return new RetentionCleanupResult(deletedFiles, failedFiles, skippedReparsePoints);
    }

    private static bool IsRecoverableFileException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

public sealed record RetentionCleanupResult(
    int DeletedFiles,
    int FailedFiles,
    int SkippedReparsePoints);
