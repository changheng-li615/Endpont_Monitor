using System.Text.Json;
using System.Text.Json.Serialization;
using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public sealed class FileAgentConfigurationStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly string _configurationRoot;
    private readonly string _configurationPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileAgentConfigurationStore(string configurationRoot)
    {
        _configurationRoot = StoragePaths.ResolveDataRoot(configurationRoot);
        _configurationPath = StoragePaths.EnsureUnderRoot(
            _configurationRoot,
            Path.Combine(_configurationRoot, "config.json"));
    }

    public string ConfigurationPath => _configurationPath;

    public static string GetDefaultConfigurationRoot()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("Windows LocalApplicationData is unavailable.");
        }

        return Path.Combine(localAppData, "Xugar", "EndpointMonitor");
    }

    public async Task<PersistentAgentConfigurationLoadResult> LoadAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        PersistentAgentConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveUnsafeAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateStartupEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var updated = result.Configuration.Clone();
            updated.Startup.Enabled = enabled;
            await SaveUnsafeAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<PersistentAgentConfigurationLoadResult> LoadUnsafeAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_configurationPath))
        {
            return new PersistentAgentConfigurationLoadResult(new(), false, null);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configurationPath, cancellationToken)
                .ConfigureAwait(false);
            var configuration = JsonSerializer.Deserialize<PersistentAgentConfiguration>(json, JsonOptions)
                ?? throw new InvalidDataException("Persistent Agent configuration is empty.");
            var errors = configuration.GetValidationErrors();
            if (errors.Count > 0)
            {
                throw new InvalidDataException(string.Join(Environment.NewLine, errors));
            }

            return new PersistentAgentConfigurationLoadResult(configuration, false, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            PreserveMalformedFile();
            return new PersistentAgentConfigurationLoadResult(
                new PersistentAgentConfiguration(),
                true,
                "Malformed persistent Agent configuration was quarantined; safe defaults are active.");
        }
    }

    private async Task SaveUnsafeAsync(
        PersistentAgentConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var errors = configuration.GetValidationErrors();
        if (errors.Count > 0)
        {
            throw new SettingsValidationException(errors);
        }

        var json = JsonSerializer.Serialize(configuration, JsonOptions);
        await AtomicFile.WriteAllTextAsync(
            _configurationRoot,
            _configurationPath,
            json,
            cancellationToken).ConfigureAwait(false);
    }

    private void PreserveMalformedFile()
    {
        try
        {
            var corruptPath = StoragePaths.EnsureUnderRoot(
                _configurationRoot,
                Path.Combine(
                    _configurationRoot,
                    $"config.corrupt.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{Guid.NewGuid():N}.json"));
            File.Move(_configurationPath, corruptPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Safe defaults are still preferable to loading malformed or secret-bearing JSON.
        }
    }
}
