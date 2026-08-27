using System.Globalization;

namespace Xugar.Endpoint.Core.Models;

public sealed class PersistentAgentConfiguration
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public PersistentServerSyncConfiguration ServerSync { get; set; } = new();

    public PersistentStartupConfiguration Startup { get; set; } = new();

    public PersistentAgentConfiguration Clone() => new()
    {
        Version = Version,
        ServerSync = ServerSync.Clone(),
        Startup = new PersistentStartupConfiguration { Enabled = Startup.Enabled }
    };

    public IReadOnlyDictionary<string, string?> ToConfigurationValues() =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ServerSync:Enabled"] = ServerSync.Enabled.ToString(CultureInfo.InvariantCulture),
            ["ServerSync:BaseUrl"] = ServerSync.BaseUrl,
            ["ServerSync:AllowInsecureLocalhost"] = ServerSync.AllowInsecureLocalhost.ToString(CultureInfo.InvariantCulture),
            ["ServerSync:HeartbeatIntervalSeconds"] = ServerSync.HeartbeatIntervalSeconds.ToString(CultureInfo.InvariantCulture),
            ["ServerSync:PolicyRefreshIntervalSeconds"] = ServerSync.PolicyRefreshIntervalSeconds.ToString(CultureInfo.InvariantCulture),
            ["ServerSync:PolicyMaxAgeSeconds"] = ServerSync.PolicyMaxAgeSeconds.ToString(CultureInfo.InvariantCulture),
            ["ServerSync:UploadBatchSize"] = ServerSync.UploadBatchSize.ToString(CultureInfo.InvariantCulture),
            ["ServerSync:QueueMaxItems"] = ServerSync.QueueMaxItems.ToString(CultureInfo.InvariantCulture),
            ["ServerSync:QueueMaxBytes"] = ServerSync.QueueMaxBytes.ToString(CultureInfo.InvariantCulture),
            ["ServerSync:QueueMaxAgeHours"] = ServerSync.QueueMaxAgeHours.ToString(CultureInfo.InvariantCulture),
            ["ServerSync:RequestTimeoutSeconds"] = ServerSync.RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            ["ServerSync:RetryMinimumSeconds"] = ServerSync.RetryMinimumSeconds.ToString(CultureInfo.InvariantCulture),
            ["ServerSync:RetryMaximumSeconds"] = ServerSync.RetryMaximumSeconds.ToString(CultureInfo.InvariantCulture)
        };

    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (Version != CurrentVersion)
        {
            errors.Add($"Persistent configuration version must be {CurrentVersion}.");
        }
        if (ServerSync is null)
        {
            errors.Add("Persistent ServerSync configuration is required.");
        }
        else
        {
            if (!Uri.TryCreate(ServerSync.BaseUrl, UriKind.Absolute, out _))
            {
                errors.Add("Persistent ServerSync:BaseUrl must be an absolute URL.");
            }
            errors.AddRange(ServerSync.ToRuntimeSettings().GetValidationErrors());
        }
        if (Startup is null)
        {
            errors.Add("Persistent Startup configuration is required.");
        }
        return errors;
    }
}

public sealed class PersistentServerSyncConfiguration
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://localhost:3000";

    public bool AllowInsecureLocalhost { get; set; } = true;

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

    public PersistentServerSyncConfiguration Clone() => (PersistentServerSyncConfiguration)MemberwiseClone();

    public ServerSyncSettings ToRuntimeSettings() => new()
    {
        Enabled = Enabled,
        BaseUrl = BaseUrl,
        AllowInsecureLocalhost = AllowInsecureLocalhost,
        EnrollmentToken = string.Empty,
        HeartbeatIntervalSeconds = HeartbeatIntervalSeconds,
        PolicyRefreshIntervalSeconds = PolicyRefreshIntervalSeconds,
        PolicyMaxAgeSeconds = PolicyMaxAgeSeconds,
        UploadBatchSize = UploadBatchSize,
        QueueMaxItems = QueueMaxItems,
        QueueMaxBytes = QueueMaxBytes,
        QueueMaxAgeHours = QueueMaxAgeHours,
        RequestTimeoutSeconds = RequestTimeoutSeconds,
        RetryMinimumSeconds = RetryMinimumSeconds,
        RetryMaximumSeconds = RetryMaximumSeconds
    };
}

public sealed class PersistentStartupConfiguration
{
    public bool Enabled { get; set; }
}

public sealed record PersistentAgentConfigurationLoadResult(
    PersistentAgentConfiguration Configuration,
    bool RecoveredFromMalformedFile,
    string? Warning);
