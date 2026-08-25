using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public static class ProcessCategorizer
{
    public static ProcessCategory Categorize(
        ProcessSnapshotRecord process,
        string? windowsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (string.IsNullOrWhiteSpace(process.ExecutablePath))
        {
            return ProcessCategory.Unknown;
        }

        try
        {
            if (!Path.IsPathFullyQualified(process.ExecutablePath))
            {
                return ProcessCategory.Unknown;
            }

            var executablePath = Path.GetFullPath(process.ExecutablePath);
            var systemRoot = string.IsNullOrWhiteSpace(windowsDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                : windowsDirectory;
            if (string.IsNullOrWhiteSpace(systemRoot))
            {
                return ProcessCategory.Unknown;
            }

            return IsInsideDirectory(systemRoot, executablePath)
                ? ProcessCategory.System
                : ProcessCategory.Application;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return ProcessCategory.Unknown;
        }
    }

    private static bool IsInsideDirectory(string directory, string path)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var relativePath = Path.GetRelativePath(fullDirectory, path);
        return !Path.IsPathRooted(relativePath) &&
               !relativePath.Equals("..", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
