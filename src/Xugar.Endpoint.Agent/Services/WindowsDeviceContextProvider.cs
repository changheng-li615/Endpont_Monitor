using System.Reflection;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Agent.Services;

public sealed class WindowsDeviceContextProvider : IDeviceContextProvider
{
    public DeviceContext GetCurrent(DateTimeOffset capturedAtUtc)
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var applicationVersion = entryAssembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? entryAssembly?.GetName().Version?.ToString()
            ?? "unknown";

        var userName = string.IsNullOrWhiteSpace(Environment.UserDomainName)
            ? Environment.UserName
            : $@"{Environment.UserDomainName}\{Environment.UserName}";

        return new DeviceContext(
            capturedAtUtc,
            Environment.MachineName,
            userName,
            Environment.OSVersion.VersionString,
            applicationVersion);
    }
}
