namespace Xugar.Endpoint.Core.Interfaces;

public interface IStartupRegistry
{
    string? GetValue(string valueName);

    void SetValue(string valueName, string command);

    void DeleteValue(string valueName);
}
