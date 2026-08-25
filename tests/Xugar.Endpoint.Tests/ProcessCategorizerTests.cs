using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class ProcessCategorizerTests
{
    [Fact]
    public void CategorizationIsConservativeAndPathBased()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var windowsDirectory = Path.Combine(temporaryDirectory.Path, "Windows");
        var systemProcess = CreateProcess(Path.Combine(windowsDirectory, "System32", "system-tool.exe"));
        var applicationProcess = CreateProcess(
            Path.Combine(temporaryDirectory.Path, "Applications", "work-app.exe"));
        var inaccessibleProcess = CreateProcess(executablePath: null);

        Assert.Equal(
            ProcessCategory.System,
            ProcessCategorizer.Categorize(systemProcess, windowsDirectory));
        Assert.Equal(
            ProcessCategory.Application,
            ProcessCategorizer.Categorize(applicationProcess, windowsDirectory));
        Assert.Equal(
            ProcessCategory.Unknown,
            ProcessCategorizer.Categorize(inaccessibleProcess, windowsDirectory));
    }

    private static ProcessSnapshotRecord CreateProcess(string? executablePath) =>
        new("test", 1, executablePath, null, null, null, null);
}
