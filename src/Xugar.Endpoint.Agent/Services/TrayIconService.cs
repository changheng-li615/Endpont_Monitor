using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Xugar.Endpoint.Agent.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly MonitoringCoordinator _monitoringCoordinator;
    private readonly AgentSynchronizationCoordinator _synchronizationCoordinator;
    private readonly DataFolderLauncher _dataFolderLauncher;
    private readonly Action _openWindow;
    private readonly Func<Task> _exitApplication;
    private readonly Forms.ContextMenuStrip _contextMenu;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private string _monitoringStatus = "Starting";
    private SynchronizationProgress _synchronizationProgress;
    private bool _disposed;

    public TrayIconService(
        MonitoringCoordinator monitoringCoordinator,
        AgentSynchronizationCoordinator synchronizationCoordinator,
        DataFolderLauncher dataFolderLauncher,
        Action openWindow,
        Func<Task> exitApplication)
    {
        _monitoringCoordinator = monitoringCoordinator;
        _synchronizationCoordinator = synchronizationCoordinator;
        _dataFolderLauncher = dataFolderLauncher;
        _openWindow = openWindow;
        _exitApplication = exitApplication;
        _synchronizationProgress = synchronizationCoordinator.CurrentProgress;

        var titleItem = new Forms.ToolStripMenuItem("Xugar Endpoint Monitor") { Enabled = false };
        _statusItem = new Forms.ToolStripMenuItem("Status: Starting") { Enabled = false };
        var openItem = new Forms.ToolStripMenuItem("Open Xugar Monitor");
        openItem.Click += (_, _) => _openWindow();
        var dataItem = new Forms.ToolStripMenuItem("Open Data Folder");
        dataItem.Click += (_, _) => OpenDataFolder();
        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += async (_, _) => await ExitAsync();

        _contextMenu = new Forms.ContextMenuStrip();
        _contextMenu.Items.Add(titleItem);
        _contextMenu.Items.Add(new Forms.ToolStripSeparator());
        _contextMenu.Items.Add(_statusItem);
        _contextMenu.Items.Add(openItem);
        _contextMenu.Items.Add(dataItem);
        _contextMenu.Items.Add(new Forms.ToolStripSeparator());
        _contextMenu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _contextMenu,
            Icon = SystemIcons.Information,
            Text = "Xugar Endpoint Monitor - Starting",
            Visible = false
        };
        _notifyIcon.DoubleClick += (_, _) => _openWindow();
        _notifyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == Forms.MouseButtons.Left)
            {
                _openWindow();
            }
        };

        _monitoringCoordinator.ProgressChanged += MonitoringCoordinator_ProgressChanged;
        _synchronizationCoordinator.ProgressChanged += SynchronizationCoordinator_ProgressChanged;
    }

    public void Show()
    {
        ThrowIfDisposed();
        UpdateDisplay();
        _notifyIcon.Visible = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _monitoringCoordinator.ProgressChanged -= MonitoringCoordinator_ProgressChanged;
        _synchronizationCoordinator.ProgressChanged -= SynchronizationCoordinator_ProgressChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _disposed = true;
    }

    private void MonitoringCoordinator_ProgressChanged(object? sender, MonitoringProgress progress)
    {
        _monitoringStatus = progress.Status;
        DispatchUpdate();
    }

    private void SynchronizationCoordinator_ProgressChanged(
        object? sender,
        SynchronizationProgress progress)
    {
        _synchronizationProgress = progress;
        DispatchUpdate();
    }

    private void DispatchUpdate()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }
        _ = dispatcher.InvokeAsync(UpdateDisplay);
    }

    private void UpdateDisplay()
    {
        if (_disposed)
        {
            return;
        }

        var status = _synchronizationProgress.Enabled
            ? _synchronizationProgress.ServerStatus
            : _monitoringStatus;
        _statusItem.Text = $"Status: {status}";
        var tooltip = $"Xugar Endpoint Monitor - {status}";
        _notifyIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];
    }

    private void OpenDataFolder()
    {
        var error = _dataFolderLauncher.TryOpen();
        if (error is not null)
        {
            System.Windows.MessageBox.Show(
                error,
                "Xugar Endpoint Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task ExitAsync()
    {
        try
        {
            await _exitApplication();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Xugar Endpoint Monitor could not exit cleanly.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Xugar Endpoint Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
