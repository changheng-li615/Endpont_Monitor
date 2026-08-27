using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Xugar.Endpoint.Agent.Services;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;
using MediaColor = System.Windows.Media.Color;

namespace Xugar.Endpoint.Agent;

public partial class MainWindow : Window
{
    private readonly MonitoringCoordinator _coordinator;
    private readonly AgentSynchronizationCoordinator _synchronizationCoordinator;
    private readonly AgentLifecycleState _lifecycle;
    private readonly DataFolderLauncher _dataFolderLauncher;
    private readonly StartupRegistrationManager _startupRegistration;
    private readonly FileAgentConfigurationStore _configurationStore;
    private readonly string? _executablePath = Environment.ProcessPath;
    private bool _allowClose;

    public MainWindow(
        MonitoringCoordinator coordinator,
        AgentSynchronizationCoordinator synchronizationCoordinator,
        AgentConfiguration configuration,
        AgentLifecycleState lifecycle,
        DataFolderLauncher dataFolderLauncher,
        StartupRegistrationManager startupRegistration,
        FileAgentConfigurationStore configurationStore)
    {
        _coordinator = coordinator;
        _synchronizationCoordinator = synchronizationCoordinator;
        _lifecycle = lifecycle;
        _dataFolderLauncher = dataFolderLauncher;
        _startupRegistration = startupRegistration;
        _configurationStore = configurationStore;
        InitializeComponent();

        ScreenshotIntervalText.Text = $"{configuration.Monitoring.ScreenshotIntervalSeconds} seconds";
        ProcessIntervalText.Text = $"{configuration.Monitoring.ProcessIntervalSeconds} seconds";
        DataDirectoryText.Text = _dataFolderLauncher.DataRoot;
        ApplySynchronizationProgress(_synchronizationCoordinator.CurrentProgress);
        SetRunningState(isRunning: false);
        RefreshStartupState();
        RefreshRuntimeMode();

        _coordinator.ProgressChanged += Coordinator_ProgressChanged;
        _synchronizationCoordinator.ProgressChanged += SynchronizationCoordinator_ProgressChanged;
        IsVisibleChanged += MainWindow_IsVisibleChanged;
    }

    public void PrepareForApplicationExit() => _allowClose = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && !_lifecycle.ExitRequested)
        {
            e.Cancel = true;
            _lifecycle.HideWindow();
            Hide();
            RefreshRuntimeMode();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _coordinator.ProgressChanged -= Coordinator_ProgressChanged;
        _synchronizationCoordinator.ProgressChanged -= SynchronizationCoordinator_ProgressChanged;
        IsVisibleChanged -= MainWindow_IsVisibleChanged;
        base.OnClosed(e);
    }

    private void SynchronizationCoordinator_ProgressChanged(
        object? sender,
        SynchronizationProgress progress)
    {
        _ = Dispatcher.InvokeAsync(() => ApplySynchronizationProgress(progress));
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
        var error = _dataFolderLauncher.TryOpen();
        if (error is not null)
        {
            DetailText.Text = error;
        }
    }

    private async void StartupCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_executablePath is null)
        {
            StartupCheckBox.IsChecked = false;
            StartupCheckBox.IsEnabled = false;
            DetailText.Text = "The Agent executable path is unavailable; Windows startup could not be changed.";
            return;
        }

        StartupCheckBox.IsEnabled = false;
        var enabled = StartupCheckBox.IsChecked == true;
        try
        {
            _startupRegistration.SetEnabled(enabled, _executablePath);
            await _configurationStore.UpdateStartupEnabledAsync(enabled, CancellationToken.None);
            DetailText.Text = enabled
                ? "Windows sign-in startup is enabled for this user. The tray will appear without opening this window."
                : "Windows sign-in startup is disabled for this user.";
        }
        catch (Exception exception)
        {
            RefreshStartupState();
            DetailText.Text = $"Windows sign-in startup could not be changed: {exception.Message}";
        }
        finally
        {
            StartupCheckBox.IsEnabled = _executablePath is not null;
        }
    }

    private async Task StartMonitoringAsync()
    {
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        StatusText.Text = "Starting monitoring...";
        StatusIndicator.Fill = new SolidColorBrush(MediaColor.FromRgb(228, 158, 35));

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
            isRunning ? MediaColor.FromRgb(31, 157, 85) : MediaColor.FromRgb(138, 153, 168));
    }

    private void ShowFailure(string message)
    {
        StatusText.Text = "Monitoring error";
        DetailText.Text = message;
        StatusIndicator.Fill = new SolidColorBrush(MediaColor.FromRgb(199, 62, 62));
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
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

    private void RefreshStartupState()
    {
        if (_executablePath is null)
        {
            StartupCheckBox.IsChecked = false;
            StartupCheckBox.IsEnabled = false;
            return;
        }

        try
        {
            StartupCheckBox.IsChecked = _startupRegistration.IsEnabled(_executablePath);
            StartupCheckBox.ToolTip = StartupRegistrationManager.BuildCommand(_executablePath);
        }
        catch
        {
            StartupCheckBox.IsChecked = false;
        }
    }

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _lifecycle.ShowWindow();
        }
        else if (!_lifecycle.ExitRequested)
        {
            _lifecycle.HideWindow();
        }
        RefreshRuntimeMode();
    }

    private void RefreshRuntimeMode()
    {
        RuntimeModeText.Text = _lifecycle.WindowVisible
            ? "Runtime: Window open"
            : "Runtime: Background / tray";
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
