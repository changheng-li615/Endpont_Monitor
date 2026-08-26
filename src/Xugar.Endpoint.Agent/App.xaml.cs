using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xugar.Endpoint.Agent.Services;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Agent;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = e.Args,
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Configuration.AddEnvironmentVariables(prefix: "XUGAR_");
            ApplyServerSyncEnvironmentAliases(builder.Configuration);

            var configuration = builder.Configuration.Get<AgentConfiguration>() ?? new AgentConfiguration();
            configuration.Validate();
            var dataRoot = StoragePaths.ResolveDataRoot(configuration.Storage.RootPath);

            builder.Services.AddSingleton(configuration);
            builder.Services.AddSingleton(configuration.Monitoring);
            builder.Services.AddSingleton(configuration.ServerSync);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<IDeviceContextProvider, WindowsDeviceContextProvider>();
            builder.Services.AddSingleton<IProcessSnapshotProvider, WindowsProcessSnapshotProvider>();
            builder.Services.AddSingleton<IScreenshotCapture, WindowsScreenshotCapture>();
            builder.Services.AddSingleton<ILocalTelemetryStore>(
                _ => new FileLocalTelemetryStore(dataRoot));
            builder.Services.AddSingleton<IProcessReportWriter>(
                _ => new ProcessCsvReportWriter(dataRoot));
            builder.Services.AddSingleton<IInstallationIdentityStore>(
                _ => new FileInstallationIdentityStore(dataRoot));
            builder.Services.AddSingleton<IDeviceCredentialProtector, WindowsDpapiDeviceCredentialProtector>();
            builder.Services.AddSingleton<IDeviceCredentialStore>(services =>
                new FileDeviceCredentialStore(
                    dataRoot,
                    services.GetRequiredService<IDeviceCredentialProtector>()));
            builder.Services.AddSingleton<IMonitoringPolicyCache>(
                _ => new FileMonitoringPolicyCache(dataRoot));
            builder.Services.AddSingleton<IUploadQueue>(services =>
                new FileUploadQueue(
                    dataRoot,
                    configuration.ServerSync,
                    services.GetRequiredService<TimeProvider>()));
            builder.Services.AddSingleton(_ => new HttpClient
            {
                BaseAddress = configuration.ServerSync.GetBaseUri(),
                Timeout = TimeSpan.FromSeconds(configuration.ServerSync.RequestTimeoutSeconds)
            });
            builder.Services.AddSingleton<IXugarServerClient, XugarServerClient>();
            builder.Services.AddSingleton<DeviceEnrollmentService>();
            builder.Services.AddSingleton<CentralPolicyService>();
            builder.Services.AddSingleton<UploadQueueProcessor>();
            builder.Services.AddSingleton<AgentSynchronizationCoordinator>();
            builder.Services.AddSingleton<RetentionCleanup>();
            builder.Services.AddSingleton<MonitoringCoordinator>();
            builder.Services.AddSingleton<MainWindow>();

            _host = builder.Build();
            _host.Start();

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Xugar Endpoint Monitor could not start.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Xugar Endpoint Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void ApplyServerSyncEnvironmentAliases(IConfiguration configuration)
    {
        var aliases = new Dictionary<string, string>
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

        foreach (var (environmentName, configurationKey) in aliases)
        {
            var value = Environment.GetEnvironmentVariable(environmentName);
            if (value is not null)
            {
                configuration[configurationKey] = value;
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                _host.Services
                    .GetRequiredService<MonitoringCoordinator>()
                    .StopAsync(shutdownTimeout.Token)
                    .GetAwaiter()
                    .GetResult();
                _host.StopAsync(shutdownTimeout.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // The timeout prevents application shutdown from hanging indefinitely.
            }
            finally
            {
                _host.Dispose();
            }
        }

        base.OnExit(e);
    }
}
