using System.Windows;
using LocalMeetingNotes.App.Bootstrap;
using LocalMeetingNotes.App.Settings;
using LocalMeetingNotes.App.ViewModels;
using LocalMeetingNotes.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMeetingNotes.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _services = AppServices.Build();
            var settingsStore = _services.GetRequiredService<JsonSettingsStore>();
            await settingsStore.LoadAsync();
            _services.GetRequiredService<SettingsFileWatcher>().Start();

            var recovery = _services.GetRequiredService<IRecoveryService>();
            var recoveryResult = await recovery.RecoverAsync();
            var queue = _services.GetRequiredService<ITranscriptionQueue>();
            foreach (var recovered in recoveryResult.Sessions.Where(session => session.ShouldRequeue))
            {
                queue.Enqueue(recovered.Session.SessionId);
            }

            var viewModel = _services.GetRequiredService<MainViewModel>();
            await viewModel.InitializeAsync();

            MainWindow = new MainWindow(viewModel, _services);
            MainWindow.Show();
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

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            _services.GetRequiredService<SettingsFileWatcher>().Dispose();

            if (_services.GetRequiredService<ITranscriptionQueue>() is IAsyncDisposable queue)
            {
                queue.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            if (_services.GetRequiredService<IAudioCaptureService>() is IDisposable capture)
            {
                capture.Dispose();
            }

            _services.Dispose();
        }

        base.OnExit(e);
    }
}
