namespace LocalMeetingNotes.Core.Models;

public sealed record RecordingResult(
    string MicrophoneWavPath,
    string SystemWavPath,
    bool MicrophoneCaptured,
    bool SystemCaptured,
    TimeSpan MicrophoneStartOffset,
    TimeSpan SystemStartOffset,
    TimeSpan Duration);
