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

            var configuration = builder.Configuration.Get<AgentConfiguration>() ?? new AgentConfiguration();
            configuration.Validate();
            var dataRoot = StoragePaths.ResolveDataRoot(configuration.Storage.RootPath);

            builder.Services.AddSingleton(configuration);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<IDeviceContextProvider, WindowsDeviceContextProvider>();
            builder.Services.AddSingleton<IProcessSnapshotProvider, WindowsProcessSnapshotProvider>();
            builder.Services.AddSingleton<IScreenshotCapture, WindowsScreenshotCapture>();
            builder.Services.AddSingleton<ILocalTelemetryStore>(
                _ => new FileLocalTelemetryStore(dataRoot));
            builder.Services.AddSingleton<IProcessReportWriter>(
                _ => new ProcessCsvReportWriter(dataRoot));
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
