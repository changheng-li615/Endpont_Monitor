using Xugar.Endpoint.Core.Interfaces;

namespace Xugar.Endpoint.Core.Services;

public sealed class FileInstallationIdentityStore : IInstallationIdentityStore, IDisposable
{
    private readonly string _dataRoot;
    private readonly string _identityPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileInstallationIdentityStore(string dataRoot)
    {
        _dataRoot = StoragePaths.ResolveDataRoot(dataRoot);
        _identityPath = StoragePaths.GetInstallationIdentityPath(_dataRoot);
    }

    public async Task<Guid> GetOrCreateInstallationIdAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_identityPath))
            {
                var text = await File.ReadAllTextAsync(_identityPath, cancellationToken).ConfigureAwait(false);
                if (Guid.TryParseExact(text.Trim(), "D", out var existing) && existing != Guid.Empty)
                {
                    return existing;
                }

                PreserveCorruptIdentity();
            }

            var created = Guid.NewGuid();
            await AtomicFile.WriteAllTextAsync(
                _dataRoot,
                _identityPath,
                created.ToString("D"),
                cancellationToken).ConfigureAwait(false);
            return created;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private void PreserveCorruptIdentity()
    {
        var directory = Path.GetDirectoryName(_identityPath)!;
        var corruptPath = StoragePaths.EnsureUnderRoot(
            _dataRoot,
            Path.Combine(directory, $"installation-id.corrupt.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{Guid.NewGuid():N}"));
        File.Move(_identityPath, corruptPath);
    }
}
