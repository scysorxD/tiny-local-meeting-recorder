namespace LocalMeetingNotes.Core.Models;

public sealed record RecordingRequest(
    string? MicrophoneDeviceId,
    string? RenderDeviceId,
    string MicrophoneWavPath,
    string SystemWavPath,
    bool AllowMicOnly = false,
    bool AllowSystemOnly = false);
