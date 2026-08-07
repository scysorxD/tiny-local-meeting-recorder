using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LocalMeetingNotes.App.ViewModels;
using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.App.Tray;

public sealed class TrayIconService : IDisposable
{
    private static readonly Color RecordingColor = Color.FromArgb(0xFF, 0x5F, 0x57);
    private static readonly Color IdleColor = Color.FromArgb(0x3F, 0xC1, 0x7A);

    private readonly List<IntPtr> _iconHandles = [];
    private readonly Icon _recordingIcon;
    private readonly Icon _idleIcon;
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

        _recordingIcon = CreateCircleIcon(RecordingColor);
        _idleIcon = CreateCircleIcon(IdleColor);

        _notifyIcon = new NotifyIcon
        {
            Text = "Local Meeting Notes",
            Visible = true,
            Icon = _idleIcon,
        };

        _notifyIcon.DoubleClick += (_, _) => _showMainWindow();
        _notifyIcon.ContextMenuStrip = BuildMenu();
        UpdateStatus(GlobalAppStatus.Ready, "Ready");
    }

    public void UpdateStatus(GlobalAppStatus status, string tooltip)
    {
        _notifyIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];
        _notifyIcon.Icon = status is GlobalAppStatus.Recording or GlobalAppStatus.RecordingAndTranscribing
            ? _recordingIcon
            : _idleIcon;
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
        _recordingIcon.Dispose();
        _idleIcon.Dispose();

        foreach (var handle in _iconHandles)
        {
            DestroyIcon(handle);
        }

        _iconHandles.Clear();
    }

    [DllImport("user32.dll", PreserveSig = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private Icon CreateCircleIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, 3, 3, 26, 26);
        }

        var handle = bitmap.GetHicon();
        _iconHandles.Add(handle);
        return Icon.FromHandle(handle);
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
