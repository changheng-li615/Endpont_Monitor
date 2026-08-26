using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Xugar.Endpoint.Agent.Services;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Agent;

public partial class MainWindow : Window
{
    private readonly MonitoringCoordinator _coordinator;
    private readonly AgentSynchronizationCoordinator _synchronizationCoordinator;
    private readonly string _dataRoot;
    private bool _loaded;

    public MainWindow(
        MonitoringCoordinator coordinator,
        AgentSynchronizationCoordinator synchronizationCoordinator,
        AgentConfiguration configuration)
    {
        _coordinator = coordinator;
        _synchronizationCoordinator = synchronizationCoordinator;
        _dataRoot = StoragePaths.ResolveDataRoot(configuration.Storage.RootPath);
        InitializeComponent();

        ScreenshotIntervalText.Text = $"{configuration.Monitoring.ScreenshotIntervalSeconds} seconds";
        ProcessIntervalText.Text = $"{configuration.Monitoring.ProcessIntervalSeconds} seconds";
        DataDirectoryText.Text = _dataRoot;
        ApplySynchronizationProgress(_synchronizationCoordinator.CurrentProgress);
        SetRunningState(isRunning: false);

        _coordinator.ProgressChanged += Coordinator_ProgressChanged;
        _synchronizationCoordinator.ProgressChanged += SynchronizationCoordinator_ProgressChanged;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void SynchronizationCoordinator_ProgressChanged(
        object? sender,
        SynchronizationProgress progress)
    {
        _ = Dispatcher.InvokeAsync(() => ApplySynchronizationProgress(progress));
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

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_dataRoot);
            using var explorer = Process.Start(new ProcessStartInfo
            {
                FileName = _dataRoot,
                UseShellExecute = true
            });

            if (explorer is null)
            {
                DetailText.Text = "Windows could not open the local data directory.";
            }
        }
        catch (Exception exception)
        {
            DetailText.Text = $"Could not open the local data directory: {exception.Message}";
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
        _synchronizationCoordinator.ProgressChanged -= SynchronizationCoordinator_ProgressChanged;
    }

    private void ApplySynchronizationProgress(SynchronizationProgress progress)
    {
        SyncEnrollmentText.Text = $"{(progress.Enabled ? "Enabled" : "Disabled")} / {progress.EnrollmentStatus}";
        SyncServerText.Text = progress.ServerStatus;
        SyncActivityText.Text = $"{FormatOptionalTime(progress.LastHeartbeatUtc)} / {FormatOptionalTime(progress.LastSuccessfulUploadUtc)}";
        SyncPolicyRefreshText.Text = FormatOptionalTime(progress.LastPolicyRefreshUtc);
        SyncQueueText.Text = $"{progress.PendingQueueItems} items / {FormatBytes(progress.PendingQueueBytes)}";
        SyncPolicyText.Text = progress.PolicyStatus;
    }

    private static string FormatLocalTime(DateTimeOffset timestampUtc) =>
        timestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");

    private static string FormatOptionalTime(DateTimeOffset? timestampUtc) =>
        timestampUtc is null ? "Not yet" : FormatLocalTime(timestampUtc.Value);

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:F1} MB",
        >= 1024 => $"{bytes / 1024d:F1} KB",
        _ => $"{bytes} B"
    };
}
