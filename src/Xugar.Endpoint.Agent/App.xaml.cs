using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xugar.Endpoint.Agent.Services;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Agent;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceApplicationId = "Xugar.Endpoint.Agent";
    private IHost? _host;
    private FileAgentConfigurationStore? _configurationStore;
    private SingleInstanceCoordinator? _singleInstance;
    private RegisteredWaitHandle? _activationRegistration;
    private TrayIconService? _trayIcon;
    private AgentLifecycleState? _lifecycle;
    private int _shutdownStarted;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        try
        {
            var launchOptions = AgentLaunchOptions.Parse(e.Args);
            var configurationRoot = FileAgentConfigurationStore.GetDefaultConfigurationRoot();
            if (launchOptions.ConfigureOnly)
            {
                await ConfigureAndExitAsync(configurationRoot, launchOptions);
                return;
            }

            var instanceName = SingleInstanceCoordinator.CreatePerUserName(
                SingleInstanceApplicationId,
                Environment.UserDomainName,
                Environment.UserName);
            _singleInstance = SingleInstanceCoordinator.TryAcquire(instanceName);
            if (!_singleInstance.Acquired)
            {
                if (!launchOptions.StartInBackground)
                {
                    _singleInstance.SignalExistingInstance();
                }
                _singleInstance.Dispose();
                _singleInstance = null;
                Interlocked.Exchange(ref _shutdownStarted, 1);
                Shutdown();
                return;
            }

            _lifecycle = new AgentLifecycleState();
            if (!_lifecycle.TryStartRuntime(launchOptions.StartInBackground))
            {
                throw new InvalidOperationException("The Agent runtime was already started in this process.");
            }

            _configurationStore = new FileAgentConfigurationStore(configurationRoot);
            var persistentLoad = await _configurationStore.LoadAsync(CancellationToken.None);
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = null,
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Configuration.AddInMemoryCollection(
                persistentLoad.Configuration.ToConfigurationValues());
            builder.Configuration.AddEnvironmentVariables(prefix: "XUGAR_");
            AgentEnvironmentOverrides.Apply(
                Environment.GetEnvironmentVariable,
                (key, value) => builder.Configuration[key] = value);
            if (launchOptions.ConfigurationArguments.Count > 0)
            {
                builder.Configuration.AddCommandLine(launchOptions.ConfigurationArguments.ToArray());
            }

            var configuration = builder.Configuration.Get<AgentConfiguration>() ?? new AgentConfiguration();
            configuration.Validate();
            var dataRoot = StoragePaths.ResolveDataRoot(configuration.Storage.RootPath);

            ConfigureServices(
                builder.Services,
                configuration,
                dataRoot,
                _configurationStore,
                _lifecycle);

            _host = builder.Build();
            await _host.StartAsync();
            if (persistentLoad.Warning is not null)
            {
                _host.Services.GetRequiredService<ILogger<App>>()
                    .LogWarning("{PersistentConfigurationWarning}", persistentLoad.Warning);
            }

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            var monitoringCoordinator = _host.Services.GetRequiredService<MonitoringCoordinator>();
            _trayIcon = new TrayIconService(
                monitoringCoordinator,
                _host.Services.GetRequiredService<AgentSynchronizationCoordinator>(),
                _host.Services.GetRequiredService<DataFolderLauncher>(),
                ShowMainWindow,
                RequestExplicitExitAsync);
            _trayIcon.Show();

            _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
                _singleInstance.ActivationRequested,
                (_, timedOut) =>
                {
                    if (!timedOut && !Dispatcher.HasShutdownStarted)
                    {
                        _ = Dispatcher.InvokeAsync(ShowMainWindow);
                    }
                },
                null,
                Timeout.InfiniteTimeSpan,
                executeOnlyOnce: false);

            await monitoringCoordinator.StartAsync(CancellationToken.None);
            if (!launchOptions.StartInBackground)
            {
                ShowMainWindow();
            }
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Xugar Endpoint Monitor could not start.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Xugar Endpoint Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await RequestExplicitExitAsync(exitCode: 1);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) == 0)
        {
            _lifecycle?.RequestExit();
            CleanupRuntimeAsync().GetAwaiter().GetResult();
        }
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) == 0)
        {
            _lifecycle?.RequestExit();
            CleanupRuntimeAsync().GetAwaiter().GetResult();
        }
        base.OnExit(e);
    }

    private static void ConfigureServices(
        IServiceCollection services,
        AgentConfiguration configuration,
        string dataRoot,
        FileAgentConfigurationStore configurationStore,
        AgentLifecycleState lifecycle)
    {
        services.AddSingleton(configuration);
        services.AddSingleton(configuration.Monitoring);
        services.AddSingleton(configuration.ServerSync);
        services.AddSingleton(configurationStore);
        services.AddSingleton(lifecycle);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDeviceContextProvider, WindowsDeviceContextProvider>();
        services.AddSingleton<IProcessSnapshotProvider, WindowsProcessSnapshotProvider>();
        services.AddSingleton<IScreenshotCapture, WindowsScreenshotCapture>();
        services.AddSingleton<ILocalTelemetryStore>(_ => new FileLocalTelemetryStore(dataRoot));
        services.AddSingleton<IProcessReportWriter>(_ => new ProcessCsvReportWriter(dataRoot));
        services.AddSingleton<IInstallationIdentityStore>(_ => new FileInstallationIdentityStore(dataRoot));
        services.AddSingleton<IDeviceCredentialProtector, WindowsDpapiDeviceCredentialProtector>();
        services.AddSingleton<IDeviceCredentialStore>(provider =>
            new FileDeviceCredentialStore(
                dataRoot,
                provider.GetRequiredService<IDeviceCredentialProtector>()));
        services.AddSingleton<IMonitoringPolicyCache>(_ => new FileMonitoringPolicyCache(dataRoot));
        services.AddSingleton<IUploadQueue>(provider =>
            new FileUploadQueue(
                dataRoot,
                configuration.ServerSync,
                provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(_ => new HttpClient
        {
            BaseAddress = configuration.ServerSync.GetBaseUri(),
            Timeout = TimeSpan.FromSeconds(configuration.ServerSync.RequestTimeoutSeconds)
        });
        services.AddSingleton<IXugarServerClient, XugarServerClient>();
        services.AddSingleton<DeviceEnrollmentService>();
        services.AddSingleton<CentralPolicyService>();
        services.AddSingleton<UploadQueueProcessor>();
        services.AddSingleton<AgentSynchronizationCoordinator>();
        services.AddSingleton<RetentionCleanup>();
        services.AddSingleton<MonitoringCoordinator>();
        services.AddSingleton<IStartupRegistry, WindowsCurrentUserRunRegistry>();
        services.AddSingleton<StartupRegistrationManager>();
        services.AddSingleton(_ => new DataFolderLauncher(dataRoot));
        services.AddSingleton<MainWindow>();
    }

    private async Task ConfigureAndExitAsync(
        string configurationRoot,
        AgentLaunchOptions launchOptions)
    {
        using var store = new FileAgentConfigurationStore(configurationRoot);
        var loaded = await store.LoadAsync(CancellationToken.None);
        var configuration = loaded.Configuration.Clone();
        if (launchOptions.ServerSyncEnabled is not null)
        {
            configuration.ServerSync.Enabled = launchOptions.ServerSyncEnabled.Value;
        }
        if (launchOptions.ServerBaseUrl is not null)
        {
            configuration.ServerSync.BaseUrl = launchOptions.ServerBaseUrl;
        }
        if (launchOptions.StartupEnabled is not null)
        {
            configuration.Startup.Enabled = launchOptions.StartupEnabled.Value;
        }

        await store.SaveAsync(configuration, CancellationToken.None);
        if (launchOptions.StartupEnabled is not null)
        {
            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The Agent executable path is unavailable.");
            new StartupRegistrationManager(new WindowsCurrentUserRunRegistry())
                .SetEnabled(launchOptions.StartupEnabled.Value, executablePath);
        }

        System.Windows.MessageBox.Show(
            $"Xugar Endpoint Monitor configuration was saved to:{Environment.NewLine}{store.ConfigurationPath}{Environment.NewLine}{Environment.NewLine}" +
            "No enrollment token or device secret was written to this file.",
            "Xugar Endpoint Monitor",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        Interlocked.Exchange(ref _shutdownStarted, 1);
        Shutdown();
    }

    private void ShowMainWindow()
    {
        if (_lifecycle is null || _lifecycle.ExitRequested || MainWindow is not MainWindow window)
        {
            return;
        }

        _lifecycle.ShowWindow();
        if (!window.IsVisible)
        {
            window.Show();
        }
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Activate();
        window.Focus();
    }

    private Task RequestExplicitExitAsync() => RequestExplicitExitAsync(0);

    private async Task RequestExplicitExitAsync(int exitCode)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        _lifecycle?.RequestExit();
        try
        {
            await CleanupRuntimeAsync();
        }
        finally
        {
            // Explicit Exit must terminate even if one coordinator reports a shutdown failure.
            Shutdown(exitCode);
        }
    }

    private async Task CleanupRuntimeAsync()
    {
        _activationRegistration?.Unregister(null);
        _activationRegistration = null;
        _trayIcon?.Dispose();
        _trayIcon = null;

        if (MainWindow is MainWindow window)
        {
            window.PrepareForApplicationExit();
            window.Close();
        }

        if (_host is not null)
        {
            using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await _host.Services
                    .GetRequiredService<MonitoringCoordinator>()
                    .StopAsync(shutdownTimeout.Token);
                await _host.StopAsync(shutdownTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                // The timeout prevents application shutdown from hanging indefinitely.
            }
            finally
            {
                _host.Dispose();
                _host = null;
                _configurationStore = null;
            }
        }
        else
        {
            _configurationStore?.Dispose();
            _configurationStore = null;
        }

        _singleInstance?.Dispose();
        _singleInstance = null;
    }
}
