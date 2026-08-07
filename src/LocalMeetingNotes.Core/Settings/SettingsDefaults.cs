namespace LocalMeetingNotes.Core.Settings;

public static class SettingsDefaults
{
    public const string DefaultSelectedModel = "ggml-base.bin";
    public const string DefaultLanguage = "auto";
    public const int DefaultTranscriptionThreads = 4;

    public const string MicrophoneModeDefaultCommunications = "defaultCommunications";
    public const string MicrophoneModeDevice = "device";
    public const string SystemOutputModeDefault = "default";
    public const string SystemOutputModeDevice = "device";

    public static AppSettings Create(string appBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appBaseDirectory);

        var documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        return new AppSettings
        {
            MeetingsFolder = Path.Combine(documentsFolder, "LocalMeetingNotes"),
            ModelsFolder = Path.Combine(appBaseDirectory, "models"),
            SelectedModel = DefaultSelectedModel,
            Language = DefaultLanguage,
            TranscriptionThreads = DefaultTranscriptionThreads,
            Microphone = new DeviceSelection { Mode = MicrophoneModeDefaultCommunications },
            SystemOutput = new DeviceSelection { Mode = SystemOutputModeDefault },
            DeleteAudioAfterSuccess = true,
            StartMinimized = false,
            CloseToTray = true,
        };
    }
}
