using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class PersistentAgentConfigurationTests
{
    [Fact]
    public void DefaultsAreSafeAndSynchronizationIsDisabled()
    {
        var configuration = new PersistentAgentConfiguration();

        Assert.Equal(PersistentAgentConfiguration.CurrentVersion, configuration.Version);
        Assert.False(configuration.ServerSync.Enabled);
        Assert.Equal("http://localhost:3000", configuration.ServerSync.BaseUrl);
        Assert.False(configuration.Startup.Enabled);
    }

    [Fact]
    public async Task SavesAndLoadsNonSecretSynchronizationAndStartupSettings()
    {
        using var directory = new TemporaryDirectory();
        using var store = new FileAgentConfigurationStore(directory.Path);
        var configuration = new PersistentAgentConfiguration
        {
            ServerSync = new PersistentServerSyncConfiguration
            {
                Enabled = true,
                BaseUrl = "https://monitor.example.invalid",
                HeartbeatIntervalSeconds = 90,
                PolicyRefreshIntervalSeconds = 600
            },
            Startup = new PersistentStartupConfiguration { Enabled = true }
        };

        await store.SaveAsync(configuration, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.False(loaded.RecoveredFromMalformedFile);
        Assert.True(loaded.Configuration.ServerSync.Enabled);
        Assert.Equal("https://monitor.example.invalid", loaded.Configuration.ServerSync.BaseUrl);
        Assert.Equal(90, loaded.Configuration.ServerSync.HeartbeatIntervalSeconds);
        Assert.Equal(600, loaded.Configuration.ServerSync.PolicyRefreshIntervalSeconds);
        Assert.True(loaded.Configuration.Startup.Enabled);
    }

    [Fact]
    public async Task PersistentJsonCannotContainEnrollmentOrDeviceSecrets()
    {
        using var directory = new TemporaryDirectory();
        using var store = new FileAgentConfigurationStore(directory.Path);
        await store.SaveAsync(
            new PersistentAgentConfiguration
            {
                ServerSync = new PersistentServerSyncConfiguration
                {
                    Enabled = true,
                    BaseUrl = "https://monitor.example.invalid"
                }
            },
            CancellationToken.None);

        var json = await File.ReadAllTextAsync(store.ConfigurationPath);
        Assert.DoesNotContain("EnrollmentToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DeviceSecret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedOrSecretExpandingJsonIsQuarantinedAndUsesSafeDefaults()
    {
        using var directory = new TemporaryDirectory();
        using var store = new FileAgentConfigurationStore(directory.Path);
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(
            store.ConfigurationPath,
            """
            {
              "version": 1,
              "serverSync": {
                "enabled": true,
                "baseUrl": "https://monitor.example.invalid",
                "enrollmentToken": "must-not-be-accepted"
              },
              "startup": { "enabled": true }
            }
            """);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.True(loaded.RecoveredFromMalformedFile);
        Assert.False(loaded.Configuration.ServerSync.Enabled);
        Assert.False(File.Exists(store.ConfigurationPath));
        Assert.Single(Directory.GetFiles(directory.Path, "config.corrupt.*.json"));
    }

    [Fact]
    public void EnvironmentAliasesOverridePersistentValuesWithoutChangingTheFileModel()
    {
        var configuration = new PersistentAgentConfiguration();
        var values = configuration.ToConfigurationValues()
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var environment = new Dictionary<string, string?>
        {
            ["XUGAR_SERVER_SYNC_ENABLED"] = "true",
            ["XUGAR_SERVER_BASE_URL"] = "https://override.example.invalid"
        };

        AgentEnvironmentOverrides.Apply(
            name => environment.GetValueOrDefault(name),
            (key, value) => values[key] = value);

        Assert.Equal("true", values["ServerSync:Enabled"]);
        Assert.Equal("https://override.example.invalid", values["ServerSync:BaseUrl"]);
        Assert.DoesNotContain(
            configuration.ToConfigurationValues().Keys,
            key => key.Contains("EnrollmentToken", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StartupPreferenceCanBeUpdatedWithoutLosingServerSettings()
    {
        using var directory = new TemporaryDirectory();
        using var store = new FileAgentConfigurationStore(directory.Path);
        await store.SaveAsync(
            new PersistentAgentConfiguration
            {
                ServerSync = new PersistentServerSyncConfiguration
                {
                    Enabled = true,
                    BaseUrl = "https://monitor.example.invalid"
                }
            },
            CancellationToken.None);

        await store.UpdateStartupEnabledAsync(true, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.True(loaded.Configuration.Startup.Enabled);
        Assert.True(loaded.Configuration.ServerSync.Enabled);
        Assert.Equal("https://monitor.example.invalid", loaded.Configuration.ServerSync.BaseUrl);
    }
}
