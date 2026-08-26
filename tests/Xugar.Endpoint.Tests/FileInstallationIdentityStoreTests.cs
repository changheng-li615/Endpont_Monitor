using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class FileInstallationIdentityStoreTests
{
    [Fact]
    public async Task CreatesStableGuidAndSurvivesStoreReload()
    {
        using var directory = new TemporaryDirectory();
        Guid first;
        using (var store = new FileInstallationIdentityStore(directory.Path))
        {
            first = await store.GetOrCreateInstallationIdAsync(CancellationToken.None);
        }

        using var reloaded = new FileInstallationIdentityStore(directory.Path);
        var second = await reloaded.GetOrCreateInstallationIdAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task PreservesCorruptStateAndRecoversWithNewGuid()
    {
        using var directory = new TemporaryDirectory();
        var path = StoragePaths.GetInstallationIdentityPath(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "not-a-guid");

        using var store = new FileInstallationIdentityStore(directory.Path);
        var recovered = await store.GetOrCreateInstallationIdAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, recovered);
        Assert.Equal(recovered.ToString("D"), await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "installation-id.corrupt.*"));
    }
}
