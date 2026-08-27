namespace Xugar.Endpoint.Core.Models;

public sealed record AgentLaunchOptions(
    bool StartInBackground,
    bool ConfigureOnly,
    bool? ServerSyncEnabled,
    string? ServerBaseUrl,
    bool? StartupEnabled,
    IReadOnlyList<string> ConfigurationArguments)
{
    public static AgentLaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        var startInBackground = false;
        var configureOnly = false;
        bool? serverSyncEnabled = null;
        string? serverBaseUrl = null;
        bool? startupEnabled = null;
        var configurationArguments = new List<string>();

        for (var index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index].ToLowerInvariant())
            {
                case "--startup":
                case "--background":
                    startInBackground = true;
                    break;
                case "--configure":
                    configureOnly = true;
                    break;
                case "--enable-sync":
                    serverSyncEnabled = true;
                    break;
                case "--disable-sync":
                    serverSyncEnabled = false;
                    break;
                case "--enable-startup":
                    startupEnabled = true;
                    break;
                case "--disable-startup":
                    startupEnabled = false;
                    break;
                case "--server-url":
                    if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
                    {
                        throw new ArgumentException("--server-url requires an absolute URL value.");
                    }
                    serverBaseUrl = arguments[index];
                    break;
                default:
                    configurationArguments.Add(arguments[index]);
                    break;
            }
        }

        if (!configureOnly &&
            (serverSyncEnabled is not null || serverBaseUrl is not null || startupEnabled is not null))
        {
            throw new ArgumentException(
                "--enable-sync, --disable-sync, --server-url, and startup registration options require --configure.");
        }

        return new AgentLaunchOptions(
            startInBackground,
            configureOnly,
            serverSyncEnabled,
            serverBaseUrl,
            startupEnabled,
            configurationArguments);
    }
}
