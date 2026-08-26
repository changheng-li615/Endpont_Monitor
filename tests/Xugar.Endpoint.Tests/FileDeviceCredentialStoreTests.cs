using System.Security.Cryptography;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class FileDeviceCredentialStoreTests
{
    [Fact]
    public async Task SavesLoadsAndClearsProtectedCredential()
    {
        using var directory = new TemporaryDirectory();
        var protector = new TestProtector();
        var credential = new DeviceCredential(Guid.NewGuid(), "device-secret-value");

        using (var store = new FileDeviceCredentialStore(directory.Path, protector))
        {
            await store.SaveAsync(credential, CancellationToken.None);
            Assert.Equal(credential, await store.ReadAsync(CancellationToken.None));
            await store.ClearAsync(CancellationToken.None);
            Assert.Null(await store.ReadAsync(CancellationToken.None));
        }

        var raw = Directory.EnumerateFiles(directory.Path, "*", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        Assert.DoesNotContain(raw, value => value.Contains(credential.DeviceSecret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CorruptProtectedCredentialIsPreservedAndTreatedAsMissing()
    {
        using var directory = new TemporaryDirectory();
        var path = StoragePaths.GetDeviceCredentialPath(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, [1, 2, 3]);

        using var store = new FileDeviceCredentialStore(directory.Path, new TestProtector());
        Assert.Null(await store.ReadAsync(CancellationToken.None));
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "device-credential.corrupt.*"));
    }

    private sealed class TestProtector : IDeviceCredentialProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext) =>
            plaintext.ToArray().Select(value => (byte)(value ^ 0xA5)).ToArray();

        public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
        {
            if (protectedData.Length < 4)
            {
                throw new CryptographicException("Invalid test payload.");
            }

            return Protect(protectedData);
        }
    }
}
