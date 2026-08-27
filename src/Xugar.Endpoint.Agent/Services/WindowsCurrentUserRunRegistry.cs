using Microsoft.Win32;
using Xugar.Endpoint.Core.Interfaces;

namespace Xugar.Endpoint.Agent.Services;

public sealed class WindowsCurrentUserRunRegistry : IStartupRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? GetValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void SetValue(string valueName, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("The current-user Windows startup key is unavailable.");
        key.SetValue(valueName, command, RegistryValueKind.String);
    }

    public void DeleteValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
