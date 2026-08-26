namespace Xugar.Endpoint.Core.Models;

public sealed record DeviceCredential(Guid DeviceId, string DeviceSecret)
{
    public void Validate()
    {
        if (DeviceId == Guid.Empty)
        {
            throw new ArgumentException("A device ID is required.", nameof(DeviceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(DeviceSecret);
    }
}
