namespace LocalMeetingNotes.Core.Settings;

public sealed class AppSettings
{
    public string MeetingsFolder { get; set; } = string.Empty;

    public string ModelsFolder { get; set; } = string.Empty;

    public string SelectedModel { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public int TranscriptionThreads { get; set; }

    public DeviceSelection Microphone { get; set; } = new();

    public DeviceSelection SystemOutput { get; set; } = new();

    public bool DeleteAudioAfterSuccess { get; set; }

    public bool StartMinimized { get; set; }

    public bool CloseToTray { get; set; }
}
