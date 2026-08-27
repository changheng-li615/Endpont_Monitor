using Xugar.Endpoint.Core.Interfaces;

namespace Xugar.Endpoint.Core.Services;

public sealed class StartupRegistrationManager(IStartupRegistry registry)
{
    public const string ValueName = "XugarEndpointMonitor";

    public bool IsEnabled(string executablePath)
    {
        var expected = BuildCommand(executablePath);
        return string.Equals(registry.GetValue(ValueName), expected, StringComparison.Ordinal);
    }

    public bool SetEnabled(bool enabled, string executablePath)
    {
        var current = registry.GetValue(ValueName);
        if (enabled)
        {
            var expected = BuildCommand(executablePath);
            if (string.Equals(current, expected, StringComparison.Ordinal))
            {
                return false;
            }
            registry.SetValue(ValueName, expected);
            return true;
        }

        if (current is null)
        {
            return false;
        }
        registry.DeleteValue(ValueName);
        return true;
    }

    public static string BuildCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (executablePath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("The Agent executable path cannot contain a quote.", nameof(executablePath));
        }
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("The Agent executable path must be fully qualified.", nameof(executablePath));
        }

        return $"\"{Path.GetFullPath(executablePath)}\" --startup";
    }
}
