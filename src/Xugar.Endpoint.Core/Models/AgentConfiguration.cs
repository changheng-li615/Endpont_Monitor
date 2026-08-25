using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Core.Models;

public sealed class AgentConfiguration
{
    public MonitoringSettings Monitoring { get; set; } = new();

    public StorageSettings Storage { get; set; } = new();

    public void Validate()
    {
        var errors = new List<string>();
        errors.AddRange(Monitoring.GetValidationErrors());

        if (string.IsNullOrWhiteSpace(Storage.RootPath))
        {
            errors.Add("Storage:RootPath is required.");
        }
        else
        {
            try
            {
                _ = StoragePaths.ResolveDataRoot(Storage.RootPath);
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
            }
        }

        if (errors.Count > 0)
        {
            throw new SettingsValidationException(errors);
        }
    }
}

public sealed class MonitoringSettings
{
    public const int DefaultScreenshotIntervalSeconds = 300;
    public const int DefaultProcessIntervalSeconds = 60;
    public const int DefaultRetentionHours = 24;

    public int ScreenshotIntervalSeconds { get; set; } = DefaultScreenshotIntervalSeconds;

    public int ProcessIntervalSeconds { get; set; } = DefaultProcessIntervalSeconds;

    public int RetentionHours { get; set; } = DefaultRetentionHours;

    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (ScreenshotIntervalSeconds is < 5 or > 86_400)
        {
            errors.Add("Monitoring:ScreenshotIntervalSeconds must be between 5 and 86400.");
        }

        if (ProcessIntervalSeconds is < 5 or > 86_400)
        {
            errors.Add("Monitoring:ProcessIntervalSeconds must be between 5 and 86400.");
        }

        if (RetentionHours is < 1 or > 8_760)
        {
            errors.Add("Monitoring:RetentionHours must be between 1 and 8760.");
        }

        return errors;
    }
}

public sealed class StorageSettings
{
    public string RootPath { get; set; } = @"%LOCALAPPDATA%\Xugar\EndpointMonitor\Data";
}

public sealed class SettingsValidationException(IReadOnlyList<string> errors)
    : Exception(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
