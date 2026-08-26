using System.Text;

namespace Xugar.Endpoint.Core.Services;

public static class AtomicFile
{
    public static async Task WriteAllTextAsync(
        string dataRoot,
        string destinationPath,
        string content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var safeDestination = StoragePaths.EnsureUnderRoot(dataRoot, destinationPath);
        var directory = Path.GetDirectoryName(safeDestination)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = StoragePaths.EnsureUnderRoot(
            dataRoot,
            Path.Combine(directory, $".{Path.GetFileName(safeDestination)}.{Guid.NewGuid():N}.tmp"));
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, safeDestination, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Retention can remove an abandoned temporary file after an interrupted write.
            }
        }
    }

    public static async Task WriteAllBytesAsync(
        string dataRoot,
        string destinationPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var safeDestination = StoragePaths.EnsureUnderRoot(dataRoot, destinationPath);
        var directory = Path.GetDirectoryName(safeDestination)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = StoragePaths.EnsureUnderRoot(
            dataRoot,
            Path.Combine(directory, $".{Path.GetFileName(safeDestination)}.{Guid.NewGuid():N}.tmp"));
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content.ToArray(), cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, safeDestination, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Retention can remove an abandoned temporary file after an interrupted write.
            }
        }
    }
}
