using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Interfaces;

public interface IDeviceContextProvider
{
    DeviceContext GetCurrent(DateTimeOffset capturedAtUtc);
}
