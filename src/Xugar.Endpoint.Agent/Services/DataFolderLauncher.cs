using System.Diagnostics;
using System.IO;

namespace Xugar.Endpoint.Agent.Services;

public sealed class DataFolderLauncher(string dataRoot)
{
    public string DataRoot { get; } = dataRoot;

    public string? TryOpen()
    {
        try
        {
            Directory.CreateDirectory(DataRoot);
            using var explorer = Process.Start(new ProcessStartInfo
            {
                FileName = DataRoot,
                UseShellExecute = true
            });
            return explorer is null ? "Windows could not open the local data directory." : null;
        }
        catch (Exception exception)
        {
            return $"Could not open the local data directory: {exception.Message}";
        }
    }
}
