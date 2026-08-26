using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Tests;

public sealed class MonitoringSettingsTests
{
    [Fact]
    public void DefaultsMatchTheCommittedPhaseOnePolicy()
    {
        var settings = new MonitoringSettings();

        Assert.Equal(300, settings.ScreenshotIntervalSeconds);
        Assert.Equal(60, settings.ProcessIntervalSeconds);
        Assert.Equal(24, settings.RetentionHours);
    }

    [Fact]
    public void ValidConfigurationPassesValidation()
    {
        var configuration = new AgentConfiguration
        {
            Monitoring = new MonitoringSettings
            {
                ScreenshotIntervalSeconds = 300,
                ProcessIntervalSeconds = 60,
                RetentionHours = 24
            },
            Storage = new StorageSettings
            {
                RootPath = Path.Combine(Path.GetTempPath(), "Xugar", "EndpointMonitor", "Data")
            }
        };

        configuration.Validate();
    }

    [Theory]
    [InlineData(0, 60, 24)]
    [InlineData(300, 0, 24)]
    [InlineData(300, 60, 0)]
    public void InvalidIntervalsOrRetentionAreRejected(
        int screenshotInterval,
        int processInterval,
        int retentionHours)
    {
        var configuration = new AgentConfiguration
        {
            Monitoring = new MonitoringSettings
            {
                ScreenshotIntervalSeconds = screenshotInterval,
                ProcessIntervalSeconds = processInterval,
                RetentionHours = retentionHours
            },
            Storage = new StorageSettings
            {
                RootPath = Path.Combine(Path.GetTempPath(), "Xugar", "EndpointMonitor", "Data")
            }
        };

        Assert.Throws<SettingsValidationException>(configuration.Validate);
    }

    [Fact]
    public void FilesystemRootIsRejectedAsADataRoot()
    {
        var configuration = new AgentConfiguration
        {
            Storage = new StorageSettings { RootPath = Path.GetPathRoot(Path.GetTempPath())! }
        };

        Assert.Throws<SettingsValidationException>(configuration.Validate);
    }

    [Fact]
    public void ServerSyncDefaultsDisabledAndUsesDocumentedIntervals()
    {
        var settings = new ServerSyncSettings();

        Assert.False(settings.Enabled);
        Assert.Equal(60, settings.HeartbeatIntervalSeconds);
        Assert.Equal(300, settings.PolicyRefreshIntervalSeconds);
        Assert.Equal(300, new MonitoringSettings().ScreenshotIntervalSeconds);
    }

    [Theory]
    [InlineData("http://central.example.invalid", true)]
    [InlineData("http://localhost:3000", false)]
    public void ClearTextServerUrlIsAllowedOnlyForExplicitLoopbackDevelopment(
        string baseUrl,
        bool shouldReject)
    {
        var configuration = new AgentConfiguration
        {
            Storage = new StorageSettings
            {
                RootPath = Path.Combine(Path.GetTempPath(), "Xugar", "EndpointMonitor", "Data")
            },
            ServerSync = new ServerSyncSettings
            {
                Enabled = true,
                BaseUrl = baseUrl,
                AllowInsecureLocalhost = true
            }
        };

        if (shouldReject)
        {
            Assert.Throws<SettingsValidationException>(configuration.Validate);
        }
        else
        {
            configuration.Validate();
        }
    }

    [Fact]
    public void DisabledSynchronizationDoesNotMakeStandaloneAgentDependOnServerUrl()
    {
        var configuration = new AgentConfiguration
        {
            Storage = new StorageSettings
            {
                RootPath = Path.Combine(Path.GetTempPath(), "Xugar", "EndpointMonitor", "Data")
            },
            ServerSync = new ServerSyncSettings { Enabled = false, BaseUrl = "not-a-url" }
        };

        configuration.Validate();
        Assert.Equal("localhost", configuration.ServerSync.GetBaseUri().Host);
    }
}
