using System.Security.Cryptography;
using System.Text;

namespace Xugar.Endpoint.Core.Services;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Semaphore _semaphore;
    private readonly EventWaitHandle? _activationEvent;
    private readonly string _activationEventName;
    private bool _disposed;

    private SingleInstanceCoordinator(
        Semaphore semaphore,
        bool acquired,
        EventWaitHandle? activationEvent,
        string activationEventName)
    {
        _semaphore = semaphore;
        Acquired = acquired;
        _activationEvent = activationEvent;
        _activationEventName = activationEventName;
    }

    public bool Acquired { get; }

    public WaitHandle ActivationRequested =>
        _activationEvent ?? throw new InvalidOperationException("Only the primary instance can wait for activation.");

    public static SingleInstanceCoordinator TryAcquire(string instanceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Named Agent instance coordination requires Windows.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        var semaphore = new Semaphore(1, 1, instanceName);
        var acquired = semaphore.WaitOne(0);
        var activationEventName = $"{instanceName}.Activate";
        EventWaitHandle? activationEvent = null;
        if (acquired)
        {
            activationEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                activationEventName);
        }

        return new SingleInstanceCoordinator(semaphore, acquired, activationEvent, activationEventName);
    }

    public bool SignalExistingInstance()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Named Agent instance coordination requires Windows.");
        }
        if (Acquired)
        {
            return false;
        }
        if (!EventWaitHandle.TryOpenExisting(_activationEventName, out var existing))
        {
            return false;
        }

        using (existing)
        {
            return existing.Set();
        }
    }

    public static string CreatePerUserName(string applicationId, string domainName, string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        var userScope = $"{domainName}\\{userName}".ToUpperInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(userScope));
        return $"Local\\{applicationId}.{Convert.ToHexString(digest.AsSpan(0, 8))}";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _activationEvent?.Dispose();
        if (Acquired)
        {
            _semaphore.Release();
        }
        _semaphore.Dispose();
        _disposed = true;
    }
}
