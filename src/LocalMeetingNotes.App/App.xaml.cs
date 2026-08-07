using System.Windows;
using LocalMeetingNotes.App.Bootstrap;
using LocalMeetingNotes.App.Logging;
using LocalMeetingNotes.App.Settings;
using LocalMeetingNotes.App.Tray;
using LocalMeetingNotes.App.ViewModels;
using LocalMeetingNotes.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMeetingNotes.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private Mutex? _singleInstanceMutex;
    private TrayIconService? _tray;
    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SingleInstance.TryAcquire(out _singleInstanceMutex))
        {
            System.Windows.MessageBox.Show(
                "Local Meeting Notes is already running.",
                "Local Meeting Notes",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            _services = AppServices.Build();
            var settingsStore = _services.GetRequiredService<JsonSettingsStore>();
            await settingsStore.LoadAsync();
            _services.GetRequiredService<SettingsFileWatcher>().Start();

            var logger = _services.GetRequiredService<IAppLogger>();
            logger.Info("Application starting");

            var recovery = _services.GetRequiredService<IRecoveryService>();
            var recoveryResult = await recovery.RecoverAsync();
            var queue = _services.GetRequiredService<ITranscriptionQueue>();
            foreach (var recovered in recoveryResult.Sessions.Where(session => session.ShouldRequeue))
            {
                queue.Enqueue(recovered.Session.SessionId);
            }

            var viewModel = _services.GetRequiredService<MainViewModel>();
            await viewModel.InitializeAsync();

            _tray = new TrayIconService(
                viewModel,
                showMainWindow: ShowMainWindow,
                openSettings: () => _mainWindow?.OpenSettings(),
                exit: ExitFromTray);

            _mainWindow = new MainWindow(viewModel, _services, _tray, logger);
            MainWindow = _mainWindow;

            if (settingsStore.Current.StartMinimized)
            {
                _mainWindow.WindowState = WindowState.Minimized;
                _mainWindow.Show();
                _mainWindow.Hide();
            }
            else
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            }

            logger.Info("Application ready");
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                exception.ToString(),
                "Local Meeting Notes could not start",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ExitFromTray()
    {
        _mainWindow?.ForceClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            try
            {
                _services.GetRequiredService<IAppLogger>().Info("Application exiting");
                _services.GetRequiredService<SettingsFileWatcher>().Dispose();

                if (_services.GetRequiredService<ITranscriptionQueue>() is IAsyncDisposable queue)
                {
                    queue.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }

                if (_services.GetRequiredService<IAudioCaptureService>() is IDisposable capture)
                {
                    capture.Dispose();
                }
            }
            catch
            {
                // Best-effort shutdown.
            }

            _services.Dispose();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
