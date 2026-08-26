using System.Text.Json;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public sealed class FileMonitoringPolicyCache : IMonitoringPolicyCache, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _dataRoot;
    private readonly string _cachePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileMonitoringPolicyCache(string dataRoot)
    {
        _dataRoot = StoragePaths.ResolveDataRoot(dataRoot);
        _cachePath = StoragePaths.GetPolicyCachePath(_dataRoot);
    }

    public async Task<CachedMonitoringPolicy?> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_cachePath))
            {
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_cachePath, cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<CachedMonitoringPolicy>(json, JsonOptions);
            }
            catch (JsonException)
            {
                PreserveCorruptCache();
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(CachedMonitoringPolicy policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicFile.WriteAllTextAsync(
                _dataRoot,
                _cachePath,
                JsonSerializer.Serialize(policy, JsonOptions),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private void PreserveCorruptCache()
    {
        var directory = Path.GetDirectoryName(_cachePath)!;
        var corruptPath = StoragePaths.EnsureUnderRoot(
            _dataRoot,
            Path.Combine(directory, $"policy-cache.corrupt.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{Guid.NewGuid():N}.json"));
        File.Move(_cachePath, corruptPath);
    }
}
