using System.Windows;
using System.Windows.Input;
using LocalMeetingNotes.App.Logging;
using LocalMeetingNotes.App.Tray;
using LocalMeetingNotes.App.ViewModels;
using LocalMeetingNotes.App.Views;
using LocalMeetingNotes.Core.Services;
using LocalMeetingNotes.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMeetingNotes.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IServiceProvider _services;
    private readonly TrayIconService _tray;
    private readonly IAppLogger _logger;
    private readonly DiskSpaceChecker _diskSpaceChecker;
    private bool _forceClose;

    public MainWindow(MainViewModel viewModel, IServiceProvider services, TrayIconService tray, IAppLogger logger)
    {
        InitializeComponent();
        Views.DarkTitleBar.Apply(this);
        _viewModel = viewModel;
        _services = services;
        _tray = tray;
        _logger = logger;
        _diskSpaceChecker = services.GetRequiredService<DiskSpaceChecker>();
        DataContext = viewModel;
        _viewModel.StartRequested += OnStartRequested;
        _viewModel.SettingsRequested += OnSettingsRequested;
        Closing += OnClosing;
        Closed += OnClosed;
        StateChanged += OnStateChanged;
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private async void OnStartRequested(object? sender, EventArgs e)
    {
        var disk = _diskSpaceChecker.Check(_viewModel.GetMeetingsFolder());
        if (disk.CouldCheck && !disk.IsSufficient)
        {
            var proceed = System.Windows.MessageBox.Show(
                this,
                disk.Message + "\n\nContinue anyway?",
                "Low disk space",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (proceed != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var dialog = new StartRecordingWindow(_services.GetRequiredService<StartRecordingViewModel>())
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true || dialog.Metadata is null)
        {
            return;
        }

        try
        {
            await _viewModel.StartRecordingAsync(dialog.Metadata);
            _logger.Info($"Recording started: {dialog.Metadata.Title}");
            _tray.UpdateStatus(_viewModel.ComputeGlobalStatus(), $"Recording — {dialog.Metadata.Title}");
        }
        catch (Exception exception)
        {
            _logger.Error("Unable to start recording", exception);
            System.Windows.MessageBox.Show(this, exception.Message, "Unable to start recording", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSettingsRequested(object? sender, EventArgs e) => OpenSettings();

    public void OpenSettings()
    {
        var window = new SettingsWindow(_services.GetRequiredService<SettingsViewModel>())
        {
            Owner = this,
        };
        window.ShowDialog();
        _ = _viewModel.InitializeAsync();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose)
        {
            return;
        }

        if (_viewModel.IsRecording)
        {
            var result = System.Windows.MessageBox.Show(
                this,
                "A recording is currently active.\n\nStop and save before exiting?",
                "Recording active",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK)
            {
                e.Cancel = true;
                return;
            }

            _viewModel.StopCommand.Execute(null);
        }

        if (_viewModel.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _viewModel.CloseToTray)
        {
            Hide();
        }
    }

    private void SessionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SessionsList.SelectedItem is SessionRowViewModel row)
        {
            _viewModel.OpenItemCommand.Execute(row);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.StartRequested -= OnStartRequested;
        _viewModel.SettingsRequested -= OnSettingsRequested;
        Closing -= OnClosing;
        StateChanged -= OnStateChanged;
        _viewModel.Dispose();
        _tray.Dispose();
    }
}
