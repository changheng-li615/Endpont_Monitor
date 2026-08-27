namespace Xugar.Endpoint.Core.Services;

public static class AgentEnvironmentOverrides
{
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["XUGAR_SERVER_SYNC_ENABLED"] = "ServerSync:Enabled",
            ["XUGAR_SERVER_BASE_URL"] = "ServerSync:BaseUrl",
            ["XUGAR_ALLOW_INSECURE_LOCALHOST"] = "ServerSync:AllowInsecureLocalhost",
            ["XUGAR_ENROLLMENT_TOKEN"] = "ServerSync:EnrollmentToken",
            ["XUGAR_HEARTBEAT_INTERVAL_SECONDS"] = "ServerSync:HeartbeatIntervalSeconds",
            ["XUGAR_POLICY_REFRESH_SECONDS"] = "ServerSync:PolicyRefreshIntervalSeconds",
            ["XUGAR_POLICY_MAX_AGE_SECONDS"] = "ServerSync:PolicyMaxAgeSeconds",
            ["XUGAR_UPLOAD_BATCH_SIZE"] = "ServerSync:UploadBatchSize",
            ["XUGAR_QUEUE_MAX_ITEMS"] = "ServerSync:QueueMaxItems",
            ["XUGAR_QUEUE_MAX_BYTES"] = "ServerSync:QueueMaxBytes",
            ["XUGAR_QUEUE_MAX_AGE_HOURS"] = "ServerSync:QueueMaxAgeHours"
        };

    public static void Apply(
        Func<string, string?> readEnvironment,
        Action<string, string?> setConfigurationValue)
    {
        ArgumentNullException.ThrowIfNull(readEnvironment);
        ArgumentNullException.ThrowIfNull(setConfigurationValue);

        foreach (var (environmentName, configurationKey) in Aliases)
        {
            var value = readEnvironment(environmentName);
            if (value is not null)
            {
                setConfigurationValue(configurationKey, value);
            }
        }
    }
}
