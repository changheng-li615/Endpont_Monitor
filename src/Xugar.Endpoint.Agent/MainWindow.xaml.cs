using System.Windows;
using System.Windows.Media;
using Xugar.Endpoint.Agent.Services;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Agent;

public partial class MainWindow : Window
{
    private readonly MonitoringCoordinator _coordinator;
    private bool _loaded;

    public MainWindow(MonitoringCoordinator coordinator, AgentConfiguration configuration)
    {
        _coordinator = coordinator;
        InitializeComponent();

        ScreenshotIntervalText.Text = $"{configuration.Monitoring.ScreenshotIntervalSeconds} seconds";
        ProcessIntervalText.Text = $"{configuration.Monitoring.ProcessIntervalSeconds} seconds";
        DataDirectoryText.Text = StoragePaths.ResolveDataRoot(configuration.Storage.RootPath);
        SetRunningState(isRunning: false);

        _coordinator.ProgressChanged += Coordinator_ProgressChanged;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await StartMonitoringAsync();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        await StartMonitoringAsync();
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = false;

        try
        {
            await _coordinator.StopAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            ShowFailure($"Could not stop cleanly: {exception.Message}");
        }
    }

    private async Task StartMonitoringAsync()
    {
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        StatusText.Text = "Starting monitoring…";
        StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(228, 158, 35));

        try
        {
            await _coordinator.StartAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            ShowFailure($"Could not start monitoring: {exception.Message}");
        }
    }

    private void Coordinator_ProgressChanged(object? sender, MonitoringProgress progress)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            StatusText.Text = progress.Status;
            DetailText.Text = progress.Detail;

            if (progress.LastScreenshotUtc is not null)
            {
                LastScreenshotText.Text = FormatLocalTime(progress.LastScreenshotUtc.Value);
            }

            if (progress.LastProcessSnapshotUtc is not null)
            {
                LastProcessSnapshotText.Text = FormatLocalTime(progress.LastProcessSnapshotUtc.Value);
            }

            SetRunningState(progress.IsRunning);
        });
    }

    private void SetRunningState(bool isRunning)
    {
        StartButton.IsEnabled = !isRunning;
        StopButton.IsEnabled = isRunning;
        StatusIndicator.Fill = new SolidColorBrush(
            isRunning ? Color.FromRgb(31, 157, 85) : Color.FromRgb(138, 153, 168));
    }

    private void ShowFailure(string message)
    {
        StatusText.Text = "Monitoring error";
        DetailText.Text = message;
        StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(199, 62, 62));
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _coordinator.ProgressChanged -= Coordinator_ProgressChanged;
    }

    private static string FormatLocalTime(DateTimeOffset timestampUtc) =>
        timestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
}
