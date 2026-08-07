using LocalMeetingNotes.App.Audio;
using LocalMeetingNotes.App.Settings;
using LocalMeetingNotes.App.Transcription;
using LocalMeetingNotes.App.ViewModels;
using LocalMeetingNotes.Core.Files;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Services;
using LocalMeetingNotes.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMeetingNotes.App.Bootstrap;

public static class AppServices
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        var settingsPath = JsonSettingsStore.GetDefaultSettingsFilePath();

        services.AddSingleton(_ => new JsonSettingsStore(
            settingsPath,
            () => SettingsDefaults.Create(AppContext.BaseDirectory)));
        services.AddSingleton<ISettingsStore>(provider => provider.GetRequiredService<JsonSettingsStore>());
        services.AddSingleton(provider => new SettingsFileWatcher(
            provider.GetRequiredService<JsonSettingsStore>(),
            settingsPath));

        services.AddSingleton<ISessionRepository, FileSessionRepository>();
        services.AddSingleton<IModelCatalog, ModelCatalog>();
        services.AddSingleton<IAudioDeviceService, NAudioDeviceService>();
        services.AddSingleton<IAudioCaptureService, NAudioCaptureService>();
        services.AddSingleton<ITranscriptionEngine, WhisperTranscriptionEngine>();
        services.AddSingleton<IModelLoadProbe, WhisperModelLoadProbe>();

        services.AddSingleton<SessionStateMachine>();
        services.AddSingleton<WavFileValidator>();
        services.AddSingleton<RecordingCoordinator>();
        services.AddSingleton<IMeetingNoteWriter, MeetingNoteWriter>();
        services.AddSingleton<TranscriptionPipeline>();
        services.AddSingleton<ITranscriptionQueue, TranscriptionQueue>();
        services.AddSingleton<IRecoveryService, RecoveryService>();

        services.AddSingleton<MainViewModel>();
        services.AddTransient<StartRecordingViewModel>();

        return services.BuildServiceProvider();
    }
}
