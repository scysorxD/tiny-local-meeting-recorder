using System.IO;
using System.Windows.Forms;
using LocalMeetingNotes.App.ViewModels;
using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.App.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainViewModel _viewModel;
    private readonly Action _showMainWindow;
    private readonly Action _openSettings;
    private readonly Action _exit;
    private bool _disposed;

    public TrayIconService(
        MainViewModel viewModel,
        Action showMainWindow,
        Action openSettings,
        Action exit)
    {
        _viewModel = viewModel;
        _showMainWindow = showMainWindow;
        _openSettings = openSettings;
        _exit = exit;

        _notifyIcon = new NotifyIcon
        {
            Text = "Local Meeting Notes",
            Visible = true,
            Icon = System.Drawing.SystemIcons.Application,
        };

        _notifyIcon.DoubleClick += (_, _) => _showMainWindow();
        _notifyIcon.ContextMenuStrip = BuildMenu();
        UpdateStatus(GlobalAppStatus.Ready, "Ready");
    }

    public void UpdateStatus(GlobalAppStatus status, string tooltip)
    {
        _notifyIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];
        _notifyIcon.Icon = status switch
        {
            GlobalAppStatus.Recording => System.Drawing.SystemIcons.Error,
            GlobalAppStatus.Transcribing => System.Drawing.SystemIcons.Information,
            GlobalAppStatus.RecordingAndTranscribing => System.Drawing.SystemIcons.Warning,
            GlobalAppStatus.Error => System.Drawing.SystemIcons.Exclamation,
            _ => System.Drawing.SystemIcons.Application,
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Start Recording", null, (_, _) =>
        {
            if (_viewModel.StartCommand.CanExecute(null))
            {
                _viewModel.StartCommand.Execute(null);
            }
        });
        menu.Items.Add("Stop Recording", null, async (_, _) =>
        {
            if (_viewModel.StopCommand.CanExecute(null))
            {
                await _viewModel.StopCommand.ExecuteAsync(null);
            }
        });
        menu.Items.Add("Open", null, (_, _) => _showMainWindow());
        menu.Items.Add("Open Meetings Folder", null, (_, _) =>
        {
            var folder = _viewModel.GetMeetingsFolder();
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        });
        menu.Items.Add("Settings", null, (_, _) => _openSettings());
        menu.Items.Add("Exit", null, (_, _) => _exit());
        return menu;
    }
}
