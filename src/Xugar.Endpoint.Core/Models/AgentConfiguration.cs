using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Core.Models;

public sealed class AgentConfiguration
{
    public MonitoringSettings Monitoring { get; set; } = new();

    public StorageSettings Storage { get; set; } = new();

    public ServerSyncSettings ServerSync { get; set; } = new();

    public void Validate()
    {
        var errors = new List<string>();
        errors.AddRange(Monitoring.GetValidationErrors());
        errors.AddRange(ServerSync.GetValidationErrors());

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

public sealed record ServerSyncSettings
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://localhost:3000";

    public bool AllowInsecureLocalhost { get; set; } = true;

    public string EnrollmentToken { get; set; } = string.Empty;

    public int HeartbeatIntervalSeconds { get; set; } = 60;

    public int PolicyRefreshIntervalSeconds { get; set; } = 300;

    public int PolicyMaxAgeSeconds { get; set; } = 900;

    public int UploadBatchSize { get; set; } = 100;

    public int QueueMaxItems { get; set; } = 1_000;

    public long QueueMaxBytes { get; set; } = 100 * 1024 * 1024;

    public int QueueMaxAgeHours { get; set; } = 168;

    public int RequestTimeoutSeconds { get; set; } = 30;

    public int RetryMinimumSeconds { get; set; } = 5;

    public int RetryMaximumSeconds { get; set; } = 300;

    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (Enabled)
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                errors.Add("ServerSync:BaseUrl must be an absolute HTTP or HTTPS URL.");
            }
            else if (uri.Scheme == Uri.UriSchemeHttp &&
                     (!AllowInsecureLocalhost || !IsLoopback(uri)))
            {
                errors.Add("ServerSync:BaseUrl may use HTTP only for explicitly allowed localhost development.");
            }
        }

        ValidateRange(errors, HeartbeatIntervalSeconds, 15, 86_400, "HeartbeatIntervalSeconds");
        ValidateRange(errors, PolicyRefreshIntervalSeconds, 30, 86_400, "PolicyRefreshIntervalSeconds");
        ValidateRange(errors, PolicyMaxAgeSeconds, 60, 604_800, "PolicyMaxAgeSeconds");
        ValidateRange(errors, UploadBatchSize, 1, 512, "UploadBatchSize");
        ValidateRange(errors, QueueMaxItems, 10, 100_000, "QueueMaxItems");
        if (QueueMaxBytes is < 1_048_576 or > 10_737_418_240)
        {
            errors.Add("ServerSync:QueueMaxBytes must be between 1048576 and 10737418240.");
        }
        ValidateRange(errors, QueueMaxAgeHours, 1, 8_760, "QueueMaxAgeHours");
        ValidateRange(errors, RequestTimeoutSeconds, 5, 300, "RequestTimeoutSeconds");
        ValidateRange(errors, RetryMinimumSeconds, 1, 300, "RetryMinimumSeconds");
        ValidateRange(errors, RetryMaximumSeconds, RetryMinimumSeconds, 3_600, "RetryMaximumSeconds");
        return errors;
    }

    public Uri GetBaseUri()
    {
        if (Uri.TryCreate(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri))
        {
            return uri;
        }

        if (!Enabled)
        {
            return new Uri("http://localhost/", UriKind.Absolute);
        }

        throw new InvalidOperationException("ServerSync:BaseUrl is invalid.");
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback ||
        uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase);

    private static void ValidateRange(
        ICollection<string> errors,
        int value,
        int minimum,
        int maximum,
        string name)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add($"ServerSync:{name} must be between {minimum} and {maximum}.");
        }
    }
}

public sealed class SettingsValidationException(IReadOnlyList<string> errors)
    : Exception(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
