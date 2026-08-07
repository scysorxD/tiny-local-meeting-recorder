namespace LocalMeetingNotes.Core.Settings;

public sealed class SettingsValidationResult
{
    public required bool IsValid { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }

    public required AppSettings Normalized { get; init; }
}
