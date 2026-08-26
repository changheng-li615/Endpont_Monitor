using System.Security.Cryptography;
using System.Text.Json;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public sealed class FileDeviceCredentialStore : IDeviceCredentialStore, IDisposable
{
    private readonly string _dataRoot;
    private readonly string _credentialPath;
    private readonly IDeviceCredentialProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileDeviceCredentialStore(string dataRoot, IDeviceCredentialProtector protector)
    {
        _dataRoot = StoragePaths.ResolveDataRoot(dataRoot);
        _credentialPath = StoragePaths.GetDeviceCredentialPath(_dataRoot);
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public async Task<DeviceCredential?> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_credentialPath))
            {
                return null;
            }

            try
            {
                var protectedBytes = await File.ReadAllBytesAsync(_credentialPath, cancellationToken)
                    .ConfigureAwait(false);
                var plaintext = _protector.Unprotect(protectedBytes);
                try
                {
                    var document = JsonSerializer.Deserialize<CredentialDocument>(plaintext);
                    if (document is not { Version: 1 } ||
                        !Guid.TryParse(document.DeviceId, out var deviceId) ||
                        deviceId == Guid.Empty ||
                        string.IsNullOrWhiteSpace(document.DeviceSecret))
                    {
                        throw new InvalidDataException("The protected device credential has an invalid format.");
                    }

                    return new DeviceCredential(deviceId, document.DeviceSecret);
                }
                finally
                {
                    Array.Clear(plaintext);
                }
            }
            catch (Exception exception) when (
                exception is InvalidDataException or JsonException or FormatException or CryptographicException)
            {
                PreserveCorruptCredential();
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(DeviceCredential credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        credential.Validate();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(
                new CredentialDocument(1, credential.DeviceId.ToString("D"), credential.DeviceSecret));
            try
            {
                var protectedBytes = _protector.Protect(plaintext);
                try
                {
                    await AtomicFile.WriteAllBytesAsync(
                        _dataRoot,
                        _credentialPath,
                        protectedBytes,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Array.Clear(protectedBytes);
                }
            }
            finally
            {
                Array.Clear(plaintext);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(_credentialPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private void PreserveCorruptCredential()
    {
        var directory = Path.GetDirectoryName(_credentialPath)!;
        var corruptPath = StoragePaths.EnsureUnderRoot(
            _dataRoot,
            Path.Combine(directory, $"device-credential.corrupt.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{Guid.NewGuid():N}"));
        File.Move(_credentialPath, corruptPath);
    }

    private sealed record CredentialDocument(int Version, string DeviceId, string DeviceSecret);
}
