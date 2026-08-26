using System.Globalization;

namespace Xugar.Endpoint.Core.Services;

public static class StoragePaths
{
    public static string ResolveDataRoot(string configuredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);

        var expandedPath = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
        if (expandedPath.Contains('%', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Storage:RootPath contains an unresolved environment variable: {configuredPath}",
                nameof(configuredPath));
        }

        if (!Path.IsPathFullyQualified(expandedPath))
        {
            throw new ArgumentException(
                "Storage:RootPath must resolve to a fully qualified path.",
                nameof(configuredPath));
        }

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expandedPath));
        var volumeRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(fullPath) ?? string.Empty);
        if (string.Equals(fullPath, volumeRoot, PathComparison))
        {
            throw new ArgumentException(
                "Storage:RootPath cannot be a filesystem root.",
                nameof(configuredPath));
        }

        return fullPath;
    }

    public static string EnsureUnderRoot(string dataRoot, string candidatePath)
    {
        var safeRoot = ResolveDataRoot(dataRoot);
        var fullCandidate = Path.GetFullPath(candidatePath);
        var relativePath = Path.GetRelativePath(safeRoot, fullCandidate);

        if (Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Path must stay inside the configured Xugar data root: {fullCandidate}",
                nameof(candidatePath));
        }

        return fullCandidate;
    }

    public static string GetDateDirectory(string dataRoot, DateTimeOffset timestampUtc)
    {
        var safeRoot = ResolveDataRoot(dataRoot);
        var dateSegment = timestampUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return EnsureUnderRoot(safeRoot, Path.Combine(safeRoot, dateSegment));
    }

    public static string GetTelemetryPath(string dataRoot, DateTimeOffset timestampUtc)
    {
        var dateDirectory = GetDateDirectory(dataRoot, timestampUtc);
        return EnsureUnderRoot(dataRoot, Path.Combine(dateDirectory, "telemetry.jsonl"));
    }

    public static string GetScreenshotDirectory(string dataRoot, DateTimeOffset timestampUtc)
    {
        var dateDirectory = GetDateDirectory(dataRoot, timestampUtc);
        return EnsureUnderRoot(dataRoot, Path.Combine(dateDirectory, "screenshots"));
    }

    public static string GetProcessCurrentCsvPath(string dataRoot, DateTimeOffset timestampUtc) =>
        GetDailyFilePath(dataRoot, timestampUtc, "process-current.csv");

    public static string GetProcessEventsCsvPath(string dataRoot, DateTimeOffset timestampUtc) =>
        GetDailyFilePath(dataRoot, timestampUtc, "process-events.csv");

    public static string GetProcessSummaryCsvPath(string dataRoot, DateTimeOffset timestampUtc) =>
        GetDailyFilePath(dataRoot, timestampUtc, "process-summary.csv");

    public static string GetSynchronizationDirectory(string dataRoot)
    {
        var safeRoot = ResolveDataRoot(dataRoot);
        return EnsureUnderRoot(safeRoot, Path.Combine(safeRoot, "sync"));
    }

    public static string GetInstallationIdentityPath(string dataRoot) =>
        EnsureUnderRoot(dataRoot, Path.Combine(GetSynchronizationDirectory(dataRoot), "installation-id"));

    public static string GetDeviceCredentialPath(string dataRoot) =>
        EnsureUnderRoot(dataRoot, Path.Combine(GetSynchronizationDirectory(dataRoot), "device-credential.bin"));

    public static string GetPolicyCachePath(string dataRoot) =>
        EnsureUnderRoot(dataRoot, Path.Combine(GetSynchronizationDirectory(dataRoot), "policy-cache.json"));

    public static string GetUploadQueueDirectory(string dataRoot) =>
        EnsureUnderRoot(dataRoot, Path.Combine(GetSynchronizationDirectory(dataRoot), "queue"));

    private static string GetDailyFilePath(
        string dataRoot,
        DateTimeOffset timestampUtc,
        string fileName)
    {
        var dateDirectory = GetDateDirectory(dataRoot, timestampUtc);
        return EnsureUnderRoot(dataRoot, Path.Combine(dateDirectory, fileName));
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
