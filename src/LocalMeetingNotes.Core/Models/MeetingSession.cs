namespace LocalMeetingNotes.Core.Models;

public sealed class MeetingSession
{
    public required Guid SessionId { get; set; }

    public required MeetingMetadata Metadata { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? StoppedAt { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Draft;

    public string? MicrophoneDeviceId { get; set; }

    public string? MicrophoneDeviceName { get; set; }

    public string? SystemOutputDeviceId { get; set; }

    public string? SystemOutputDeviceName { get; set; }

    public string? MicWavPath { get; set; }

    public string? SystemWavPath { get; set; }

    public long MicCaptureStartOffsetMs { get; set; }

    public long SystemCaptureStartOffsetMs { get; set; }

    public string? ModelPath { get; set; }

    public string? ModelFileName { get; set; }

    public string Language { get; set; } = "auto";

    public string? Error { get; set; }

    public ErrorCategory? ErrorCategory { get; set; }

    public int RetryCount { get; set; }

    public required string MeetingsRoot { get; set; }
}
